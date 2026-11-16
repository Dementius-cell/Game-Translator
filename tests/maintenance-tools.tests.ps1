$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$scratch = Join-Path $root ('work/maintenance-tests/' + [guid]::NewGuid().ToString('N'))
[void](New-Item -ItemType Directory -Path $scratch -Force)
function Assert($Condition, $Message) { if (!$Condition) { throw $Message } }
$report = Join-Path $scratch 'synthetic.txt'
@'
Game Translator live-session diagnostics
  SessionRestartCount: 0
  LastLiveTranslationFailureKind: (none)
Live candidate lifecycle
  DroppedOldestEvents: 2
  [1] Refresh=1 At=2026-11-02T00:00:00Z Event=OverlaySnapshotPublished OverlayTextItems=2
  [2] Refresh=2 At=2026-11-02T00:00:01Z Event=OverlaySnapshotPublished OverlayTextItems=0 SessionInputAgeMs=1000
  [3] Refresh=3 At=2026-11-02T00:00:02Z Event=OverlaySnapshotPublished OverlayTextItems=0
  [4] Refresh=4 At=2026-11-02T00:00:03Z Event=OverlaySnapshotPublished OverlayTextItems=1
  [5] Refresh=5 At=2026-11-02T00:00:04Z Event=CandidateWorkCompleted ElapsedMs=12,5 OcrText="PRIVATE_SENTINEL \" ElapsedMs=9999" TranslatedText="private"
  [5] Refresh=5 At=2026-11-02T00:00:04Z Event=CandidateWorkCompleted ElapsedMs=12,5
  [6] Refresh=5 At=2026-11-02T00:00:04Z Event=CandidateProviderRequestObserved TranslationInputText="PRIVATE_SENTINEL ProviderQueueWaitMs=9999" ProviderDiagnosticRequestId=synthetic-1 ProviderInvocationStartedAt=2026-11-02T00:00:01Z ProviderInvocationCompletedAt=2026-11-02T00:00:03Z ProviderQueueWaitMs=100.0
  [7] Refresh=5 At=2026-11-02T00:00:04Z Event=CandidateProviderRequestObserved ProviderDiagnosticRequestId=synthetic-1 ProviderQueueWaitMs=100.0
  [8] Refresh=6 At=2026-11-02T00:00:05Z Event=OverlaySnapshotPublished OverlayTextItems=0
  [9] Refresh=7 At=2026-11-02T00:00:04Z Event=OverlaySnapshotPublished OverlayTextItems=1

Other section
  [10] Refresh=8 At=2026-11-02T00:00:10Z Event=OverlaySnapshotPublished OverlayTextItems=9999
'@ | Set-Content -LiteralPath $report -Encoding UTF8
$json = & (Join-Path $root 'tools/summarize-live-diagnostics.ps1') -LiteralPath $report
$summary = ($json | ConvertFrom-Json).Reports[0]
Assert ($json -notmatch 'PRIVATE_SENTINEL|synthetic-1|9999|synthetic.txt') 'Private or unrelated data escaped.'
Assert ($summary.CompletedEmptySnapshotIntervals.Count -eq 1 -and $summary.CompletedEmptySnapshotIntervals.MaxMs -eq 2000) 'Repeated empty publications changed interval.'
Assert ($summary.OverlayCountDropsBySessionInput.Within1sOfInput -eq 1 -and $summary.OverlayCountDropsBySessionInput.InputUnknown -eq 1) 'Drops must distinguish recent input from unavailable evidence.'
Assert ($summary.Timings.CandidateWorkCompleted.P50Ms -eq 12.5) 'Locale decimal parsing failed.'
Assert ($summary.Timings.ProviderQueueWait.Count -eq 1) 'Provider attempts double-counted.'
Assert ($summary.Timings.ProviderInvocation.MaxMs -eq 2000) 'Provider duration incorrect.'
Assert ($summary.DuplicateEventsIgnored -eq 1 -and $summary.OutOfOrderEventsIgnored -eq 1) 'Ordering diagnostics incorrect.'
Assert ($summary.OpenEmptySnapshotInterval -and $summary.DroppedOldestEvents -eq 2) 'Incomplete evidence not marked.'
Assert ($summary.FinalPublishedTextItems -eq 0) 'Unrelated section affected summary.'
Assert ($summary.LastTranslationFailure -eq 'none') 'No-failure sentinel not normalized.'
$enhanced = Join-Path $scratch 'enhanced.txt'
@'
Live candidate lifecycle
  [1] Refresh=1 At=2026-11-02T00:00:00Z Event=CandidateMenuAuthorityObserved MenuDecision=ocr-started MenuQueueWaitMs=450,5 MenuPendingOcr=40
  [2] Refresh=2 At=2026-11-02T00:00:01Z Event=CandidateMenuAuthorityObserved MenuDecision=translation-invalidated-source-expired MenuHadPublishedTranslation=True
  [3] Refresh=3 At=2026-11-02T00:00:02Z Event=CandidateMenuAuthorityObserved MenuDecision=scheduler-sample MenuQueueBlockReason=ocr-capacity
  [4] Refresh=4 At=2026-11-02T00:00:03Z Event=CandidateMenuAuthorityObserved MenuDecision=PRIVATE_SENTINEL
  [5] Refresh=5 At=2026-11-02T00:00:04Z Event=CandidateMenuAuthorityObserved MenuDecision=visual-source-renewed MenuHadPublishedTranslation=True
  [6] Refresh=6 At=2026-11-02T00:00:05Z Event=CandidateDetectionCompleted RegionRefreshReason=local-change RegionDetectorCalls=2 RegionProcessedPixels=18432
  [7] Refresh=7 At=2026-11-02T00:00:06Z Event=CandidateDetectionCompleted RegionRefreshReason=unchanged RegionDetectorCalls=0 RegionProcessedPixels=0
  [8] Refresh=8 At=2026-11-02T00:00:07Z Event=CandidateDetectionCompleted RegionRefreshReason=PRIVATE_SENTINEL OcrText="RegionDetectorCalls=9999 RegionProcessedPixels=9999"
  [9] Refresh=9 At=2026-11-02T00:00:08Z Event=CandidateDetectionCompleted RegionRefreshReason=cached-source-reused RegionDetectorCalls=0 RegionProcessedPixels=0
  [10] Refresh=10 At=2026-11-02T00:00:09Z Event=CandidateDetectionCompleted RegionRefreshReason=untrusted-source RegionRefreshDetail=cached-no-baseline RegionBaselineState=no-confirmed-translation RegionBaselineAfter=discarded-after-full-scan
  [11] Refresh=11 At=2026-11-02T00:00:10Z Event=CandidateDetectionCompleted RegionRefreshReason=untrusted-source RegionRefreshDetail=PRIVATE_SENTINEL RegionBaselineState=PRIVATE_SENTINEL RegionBaselineAfter=PRIVATE_SENTINEL OcrText="RegionRefreshDetail=cached-no-baseline RegionBaselineState=available"
  [12] Refresh=12 At=2026-11-02T00:00:11Z Event=CandidateMenuAuthorityObserved MenuDecision=association-proposed
  [13] Refresh=13 At=2026-11-02T00:00:12Z Event=CandidateMenuAuthorityObserved MenuDecision=association-confirmed
  [14] Refresh=14 At=2026-11-02T00:00:13Z Event=CandidateMenuAuthorityObserved MenuDecision=translation-invalidated-association-proposed MenuHadPublishedTranslation=True
'@ | Set-Content -LiteralPath $enhanced -Encoding UTF8
$json = & (Join-Path $root 'tools/summarize-live-diagnostics.ps1') -LiteralPath $enhanced
$detail = ($json | ConvertFrom-Json).Reports[0]
Assert ($json -notmatch 'PRIVATE_SENTINEL') 'Enhanced summary leaked untrusted fields.'
Assert ($detail.PublishedTranslationInvalidations -eq 2 -and $detail.MaximumPendingOcr -eq 40) 'Menu evidence aggregation failed.'
Assert ($detail.Timings.MenuOcrQueueWait.P50Ms -eq 450.5) 'OCR queue wait incorrect.'
Assert ($detail.OcrQueueReasonSamples.'ocr-capacity' -eq 1) 'Queue reasons not counted.'
Assert ($detail.MenuDecisions.'visual-source-renewed' -eq 1 -and $detail.MenuDecisions.other -eq 1) 'Visual renewal must be counted separately from unknown decisions.'
Assert ($detail.MenuDecisions.'association-proposed' -eq 1 -and $detail.MenuDecisions.'association-confirmed' -eq 1 -and $detail.MenuDecisions.'translation-invalidated-association-proposed' -eq 1) 'Association lifecycle must remain visible without exporting arbitrary decision text.'
Assert ($detail.RegionDetectorCalls -eq 2 -and $detail.RegionProcessedPixels -eq 18432) 'Region work must exclude zero-work cycles and quoted text.'
Assert ($detail.RegionRefreshReasons.'cached-source-reused' -eq 1) 'Known source replay must be counted separately.'
Assert ($detail.RegionRefreshDetails.'cached-no-baseline' -eq 1 -and $detail.RegionRefreshDetails.other -eq 1) 'Detailed reasons must exclude quoted text and unknown content.'
Assert ($detail.RegionDetailBaselineCounts.'cached-no-baseline/no-confirmed-translation' -eq 1) 'Detailed reason must be correlated with baseline state.'
Assert ($detail.RegionBaselineAfter.'discarded-after-full-scan' -eq 1 -and $detail.RegionBaselineAfter.other -eq 1) 'Post-detection baseline state must be allowlisted.'
Assert ($detail.RegionRefreshReasons.'local-change' -eq 1 -and $detail.RegionRefreshReasons.unchanged -eq 1 -and $detail.RegionRefreshReasons.other -eq 1) 'Region reasons must be allowlisted.'
$invalid = Join-Path $scratch 'invalid.txt'
'not a report' | Set-Content -LiteralPath $invalid
$failed = $false
try { & (Join-Path $root 'tools/summarize-live-diagnostics.ps1') -LiteralPath $invalid | Out-Null } catch { $failed = $true }
Assert $failed 'Unsupported report should fail.'
$failed = $false
try { & (Join-Path $root 'tools/summarize-live-diagnostics.ps1') -LiteralPath (Join-Path $scratch 'missing.txt') | Out-Null } catch { $failed = $true }
Assert $failed 'Missing input should fail.'

$repo = Join-Path $scratch 'repo'
[void](New-Item -ItemType Directory -Path $repo)
git init --quiet $repo
if ($LASTEXITCODE) { throw 'Synthetic repository initialization failed.' }
'safe' | Set-Content -LiteralPath (Join-Path $repo 'safe.cs')
git -C $repo add -- safe.cs
& (Join-Path $root 'tools/check-publication.ps1') -RepositoryRoot $repo | Out-Null
[void](New-Item -ItemType Directory -Path (Join-Path $repo 'work'))
'private synthetic content' | Set-Content -LiteralPath (Join-Path $repo 'work/private.txt')
git -C $repo add -- work/private.txt
$failed = $false
try { & (Join-Path $root 'tools/check-publication.ps1') -RepositoryRoot $repo | Out-Null } catch { $failed = $true }
Assert $failed 'Private staged output should fail.'
Write-Output 'Maintenance checks passed: numeric/privacy parsing, timing deduplication, overlay intervals, incomplete/invalid reports and staged path protection.'
