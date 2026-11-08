param([Parameter(Mandatory)][string[]]$LiteralPath)
$ErrorActionPreference = 'Stop'
$culture = [Globalization.CultureInfo]::InvariantCulture

function Get-NumericField([string]$Line, [string]$Name) {
    $match = [regex]::Match($Line, '(?:^| )' + $Name + '=(\d+(?:[.,]\d+)?)(?= |$)')
    if ($match.Success) { return [double]::Parse($match.Groups[1].Value.Replace(',', '.'), $culture) }
    return $null
}
function Get-Distribution($Values) {
    $sorted = @($Values | Sort-Object)
    if (!$sorted.Count) { return $null }
    return [ordered]@{
        Count = $sorted.Count
        P50Ms = $sorted[[Math]::Max(0, [Math]::Ceiling($sorted.Count * 0.50) - 1)]
        P95Ms = $sorted[[Math]::Max(0, [Math]::Ceiling($sorted.Count * 0.95) - 1)]
        MaxMs = $sorted[-1]
    }
}

$reports = @()
foreach ($inputPath in $LiteralPath) {
    $file = Get-Item -LiteralPath $inputPath
    if ($file.PSIsContainer) { throw 'Expected a report file.' }
    $events = @{}
    $menuDecisions = @{}
    $queueReasons = @{}
    $publishedInvalidations = $null
    $maximumPendingOcr = $null
    $knownMenuDecisions = @('ocr-started','text-observed','text-version-changed','translation-published','text-expired',
        'text-revoked-before-geometry','ocr-retired-generation','ocr-rejected-source','ocr-rejected-geometry',
        'translation-rejected-version-or-age','scheduler-sample','geometry-no-compatible-region','geometry-ambiguous',
        'translation-invalidated-text-contradicted','translation-invalidated-ocr-failure','translation-invalidated-source-expired',
        'translation-invalidated-text-version-changed','candidate-removed-CandidateDisappeared',
        'candidate-removed-CandidatePipelineDegraded','candidate-removed-CaptureFailed')
    $knownQueueReasons = @('retired','ocr-active','no-pending-frame','source-expired','grouping-unconfirmed',
        'minimum-start-interval','visual-quiet-wait','ocr-capacity','eligible','capture-in-progress')
    $timings = @{}
    $seen = [Collections.Generic.HashSet[long]]::new()
    $providerRequests = [Collections.Generic.HashSet[string]]::new()
    $overlayGaps = [Collections.Generic.List[double]]::new()
    $emptySince = $null
    $previousOverlayCount = $null
    $overlayDrops = [ordered]@{ Within1sOfInput = 0; MoreThan1sAfterInput = 0; InputUnknown = 0 }
    $lastAt = $null
    $firstAt = $null
    $lastSequence = -1L
    $duplicateEvents = 0
    $outOfOrderEvents = 0
    $unknownEvents = 0
    $restartCount = $null
    $dropped = $null
    $truncated = $false
    $finalFailure = 'unknown'
    $inLifecycle = $false
    $knownEvents = @('PreviousOverlayHiddenForCapture','CaptureStarted','CaptureCompleted',
        'PreviousOverlayRestoredAfterCapture','CandidateDetectionStarted','CandidateDetectionCompleted',
        'CandidateDiscovered','CandidateGeometryJitterMatched','CandidateGroupingChanged',
        'CandidateGroupingAwaitingConfirmation','CandidateSourceChanged','CandidateWorkStarted',
        'CandidateProviderRequestObserved','CandidateWorkCompleted','CandidateWorkDeferredForStability',
        'CandidateWorkFailed','CandidateWorkCancelled','CandidateRemoved','OverlaySnapshotPublished',
        'CandidateEmptyOcrRetryScheduled','CandidateOcrCompleted','CandidateTranslationQueueWaitWarning',
        'CandidateTranslationCredentialsWaitWarning','CandidateTranslationResponseWaitWarning',
        'CandidateWatermarkEvidenceChanged','CandidateMenuBackgroundChanged','CandidateMenuAuthorityObserved','CaptureFailed')
    foreach ($line in [IO.File]::ReadLines($file.FullName)) {
        if ($line -match '^\[diagnostics truncated:') { $truncated = $true }
        if ($line -match '^  SessionRestartCount: (\d+)$') { $restartCount = [long]$Matches[1] }
        if ($line -match '^  LastLiveTranslationFailureKind: (\S+)$') {
            $failure = $Matches[1]
            $finalFailure = if ($failure -in @('(none)','none','None')) { 'none' }
                elseif ($failure -in @('Unknown','Configuration','Http','Throttled','Timeout','EmptyResponse','Parse','UnsupportedResponse','ProviderCode','AllProvidersFailed','Unexpected','Dns','Connection','Tls','Network','UserVerificationRequired')) { $failure }
                else { 'other' }
        }
        if ($line -eq 'Live candidate lifecycle') { $inLifecycle = $true; continue }
        if (!$inLifecycle) { continue }
        if ($line -match '^\S' -and $line -notmatch '^\[diagnostics truncated:') { $inLifecycle = $false; continue }
        if ($line -match '^  DroppedOldestEvents: (\d+)$') { $dropped = [long]$Matches[1] }
        if ($line -notmatch '^  \[(\d+)\] Refresh=\d+ At=(\S+) Event=(\w+)(?= |$)') { continue }
        $sequence = [long]$Matches[1]
        $at = [DateTimeOffset]::Parse($Matches[2], $culture)
        $kind = $Matches[3]
        if (!$seen.Add($sequence)) { $duplicateEvents++; continue }
        if ($sequence -le $lastSequence -or ($null -ne $lastAt -and $at -lt $lastAt)) { $outOfOrderEvents++; continue }
        $lastSequence = $sequence
        $lastAt = $at
        if ($null -eq $firstAt) { $firstAt = $at }
        if ($kind -notin $knownEvents) { $unknownEvents++; continue }
        $events[$kind] = 1 + $events[$kind]
        # Strip quoted OCR/translation/error strings before recognizing numeric fields.
        $stripped = [regex]::Replace($line, '"(?:\\.|[^"\\])*"', '')
        if ($kind -eq 'CandidateMenuAuthorityObserved') {
            if ($stripped -match '(?:^| )MenuDecision=(\S+)') {
                $decision = if ($Matches[1] -in $knownMenuDecisions) { $Matches[1] } else { 'other' }
                $menuDecisions[$decision] = 1 + $menuDecisions[$decision]
                if ($stripped -match '(?:^| )MenuHadPublishedTranslation=(True|False)(?= |$)') {
                    if ($null -eq $publishedInvalidations) { $publishedInvalidations = 0 }
                    if ($Matches[1] -eq 'True' -and $decision -like 'translation-invalidated-*') { $publishedInvalidations++ }
                }
                if ($decision -eq 'ocr-started') {
                    $wait = Get-NumericField $stripped 'MenuQueueWaitMs'
                    if ($null -ne $wait) {
                        if (!$timings.ContainsKey('MenuOcrQueueWait')) { $timings['MenuOcrQueueWait'] = [Collections.Generic.List[double]]::new() }
                        $timings['MenuOcrQueueWait'].Add($wait)
                    }
                }
                if ($decision -eq 'scheduler-sample' -and $stripped -match '(?:^| )MenuQueueBlockReason=(\S+)') {
                    $reason = if ($Matches[1] -in $knownQueueReasons) { $Matches[1] } else { 'other' }
                    $queueReasons[$reason] = 1 + $queueReasons[$reason]
                }
            }
            $pending = Get-NumericField $stripped 'MenuPendingOcr'
            if ($null -ne $pending -and ($null -eq $maximumPendingOcr -or $pending -gt $maximumPendingOcr)) { $maximumPendingOcr = $pending }
        }
        # Ignore an unterminated quoted suffix in a partially written report.
        $stripped = ($stripped -split '"', 2)[0]
        if ($kind -in @('CaptureCompleted','CandidateDetectionCompleted','CandidateOcrCompleted','CandidateWorkCompleted')) {
            $value = Get-NumericField $stripped 'ElapsedMs'
            if ($null -ne $value) {
                if (!$timings.ContainsKey($kind)) { $timings[$kind] = [Collections.Generic.List[double]]::new() }
                $timings[$kind].Add($value)
            }
        }
        # Exactly one queue sample per observed provider invocation, never from warning/completion copies.
        if ($kind -eq 'CandidateProviderRequestObserved') {
            $id = [regex]::Match($stripped, ' ProviderDiagnosticRequestId=(\S+)')
            $value = Get-NumericField $stripped 'ProviderQueueWaitMs'
            if ($id.Success -and $null -ne $value -and $providerRequests.Add($id.Groups[1].Value)) {
                if (!$timings.ContainsKey('ProviderQueueWait')) { $timings['ProviderQueueWait'] = [Collections.Generic.List[double]]::new() }
                $timings['ProviderQueueWait'].Add($value)
                $start = [regex]::Match($stripped, ' ProviderInvocationStartedAt=(\S+)')
                $end = [regex]::Match($stripped, ' ProviderInvocationCompletedAt=(\S+)')
                if ($start.Success -and $end.Success) {
                    $duration = ([DateTimeOffset]::Parse($end.Groups[1].Value, $culture) - [DateTimeOffset]::Parse($start.Groups[1].Value, $culture)).TotalMilliseconds
                    if ($duration -ge 0) {
                        if (!$timings.ContainsKey('ProviderInvocation')) { $timings['ProviderInvocation'] = [Collections.Generic.List[double]]::new() }
                        $timings['ProviderInvocation'].Add($duration)
                    }
                }
            }
        }
        if ($kind -eq 'OverlaySnapshotPublished') {
            $count = Get-NumericField $stripped 'OverlayTextItems'
            if ($null -eq $count) { continue }
            if ($null -ne $previousOverlayCount -and $count -lt $previousOverlayCount) {
                $age = Get-NumericField $stripped 'SessionInputAgeMs'
                if ($null -eq $age) { $overlayDrops.InputUnknown++ }
                elseif ($age -le 1000) { $overlayDrops.Within1sOfInput++ }
                else { $overlayDrops.MoreThan1sAfterInput++ }
            }
            if ($count -eq 0 -and $previousOverlayCount -gt 0) { $emptySince = $at }
            if ($count -gt 0 -and $null -ne $emptySince) {
                $overlayGaps.Add(($at - $emptySince).TotalMilliseconds)
                $emptySince = $null
            }
            $previousOverlayCount = $count
        }
    }
    if ($seen.Count -eq 0) { throw 'No supported lifecycle events found; not a complete live-session report.' }
    $distributions = [ordered]@{}
    foreach ($key in ($timings.Keys | Sort-Object)) { $distributions[$key] = Get-Distribution $timings[$key] }
    $reports += [ordered]@{
        ReportNumber = $reports.Count + 1
        SessionRestartCount = $restartCount
        LastTranslationFailure = $finalFailure
        RetainedUniqueEvents = $seen.Count
        DroppedOldestEvents = $dropped
        Truncated = $truncated
        DuplicateEventsIgnored = $duplicateEvents
        OutOfOrderEventsIgnored = $outOfOrderEvents
        UnknownEventsIgnored = $unknownEvents
        ObservedSpanMs = ($lastAt - $firstAt).TotalMilliseconds
        EventCounts = $events
        MenuDecisions = $menuDecisions
        PublishedTranslationInvalidations = $publishedInvalidations
        MaximumPendingOcr = $maximumPendingOcr
        OcrQueueReasonSamples = $queueReasons
        OverlayCountDropsBySessionInput = $overlayDrops
        Timings = $distributions
        FinalPublishedTextItems = $previousOverlayCount
        CompletedEmptySnapshotIntervals = Get-Distribution $overlayGaps
        OpenEmptySnapshotInterval = ($null -ne $emptySince)
    }
}
[ordered]@{
    SchemaVersion = 2
    Scope = 'Independent report snapshots; never sum overlapping snapshots as sessions.'
    Limit = 'Empty published snapshots are not proof of visible flicker. Initial empty state and an unfinished final interval are not completed gaps. Percentiles use nearest rank on retained events only.'
    Reports = $reports
} | ConvertTo-Json -Depth 8
