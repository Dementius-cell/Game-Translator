param(
    [switch]$Json
)

$ErrorActionPreference = 'Stop'

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..')
Push-Location $repoRoot
try {
    $markdownFiles = @(git ls-files -- '*.md')
    $tracked = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($path in @(git ls-files)) {
        [void]$tracked.Add(($path -replace '\\', '/'))
    }

    function Resolve-RepoPath {
        param(
            [string]$FromFile,
            [string]$Target
        )

        $cleanTarget = ($Target -split '#')[0]
        if ([string]::IsNullOrWhiteSpace($cleanTarget)) {
            return $null
        }

        if ($cleanTarget -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') {
            return $null
        }

        $normalized = $cleanTarget -replace '\\', '/'
        if ($normalized.StartsWith('/')) {
            return $normalized.TrimStart('/')
        }

        if ($normalized -eq 'AGENTS.md' -or
            $normalized -eq 'README.md' -or
            $normalized -eq 'GameTranslator.sln' -or
            $normalized.StartsWith('docs/') -or
            $normalized.StartsWith('tests/') -or
            $normalized.StartsWith('artifacts/') -or
            $normalized.StartsWith('src/') -or
            $normalized.StartsWith('tools/') -or
            $normalized.StartsWith('.github/')) {
            return $normalized
        }

        $fromDir = Split-Path -Parent ($FromFile -replace '\\', '/')
        if ([string]::IsNullOrEmpty($fromDir)) {
            if ($normalized -match '[*?<>|]') {
                return $normalized
            }
            return $normalized
        }

        $combined = Join-Path $fromDir $normalized
        if ($combined -match '[*?<>|]') {
            return ($combined -replace '\\', '/')
        }

        try {
            $full = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $combined))
        }
        catch {
            return ($combined -replace '\\', '/')
        }
        $rootFull = [System.IO.Path]::GetFullPath($repoRoot)
        if (-not $full.StartsWith($rootFull, [System.StringComparison]::OrdinalIgnoreCase)) {
            return $combined -replace '\\', '/'
        }

        $rootUri = [System.Uri]::new(($rootFull.TrimEnd('\') + '\'))
        $fullUri = [System.Uri]::new($full)
        return ([System.Uri]::UnescapeDataString($rootUri.MakeRelativeUri($fullUri).ToString()) -replace '\\', '/')
    }

    function Get-BacktickCategory {
        param(
            [string]$Path,
            [string]$Resolved,
            [bool]$Exists,
            [bool]$IsTracked
        )

        $normalized = $Path -replace '\\', '/'
        $resolvedNormalized = $Resolved -replace '\\', '/'

        if ($normalized -eq 'Dementius-cell/Game-Translator') {
            return 'github-repo-slug'
        }
        if ($normalized -match '^\d+(/\d+)+$') {
            return 'metric-ratio'
        }
        if ($normalized -match '[*?]' -or $resolvedNormalized -match '[*?]') {
            return 'glob-pattern'
        }
        if ($normalized -match '<[^>]+>' -or $resolvedNormalized -match '<[^>]+>') {
            return 'template-path'
        }
        if ($normalized -eq 'CONTEXT.md') {
            return 'optional-future-file'
        }
        if ($normalized -match '^outputs(/|$)' -or $resolvedNormalized -match '^outputs(/|$)') {
            return 'ignored-local-output'
        }
        if ($normalized -match '^work(/|$)' -or $resolvedNormalized -match '^work(/|$)') {
            return 'ignored-local-harness'
        }
        if ($normalized -match '(^|/)bin/' -or $normalized -match '(^|/)obj/' -or $resolvedNormalized -match '(^|/)bin/' -or $resolvedNormalized -match '(^|/)obj/' -or $normalized -eq 'GameTranslator.UI.exe') {
            return 'build-output'
        }
        if ($normalized -match '^artifacts/manual-' -or $resolvedNormalized -match '^artifacts/manual-') {
            return 'manual-smoke-local'
        }
        if ($normalized -match '^artifacts/calibration/' -or $resolvedNormalized -match '^artifacts/calibration/') {
            if ($IsTracked) {
                return 'tracked-calibration'
            }
            if ($Exists) {
                return 'generated-calibration-local'
            }
            return 'generated-calibration-missing'
        }
        if ($normalized -match '^(candidate-evidence|scorecard|fit-rules|placement-evidence-map)\.(png|json)$') {
            return 'historical-shorthand'
        }
        if ($IsTracked) {
            return 'tracked-repo-path'
        }
        if ($Exists) {
            return 'existing-untracked-local'
        }

        return 'unknown-missing'
    }

    $markdownLinkProblems = @()
    $backtickPaths = @()

    foreach ($file in $markdownFiles) {
        $lines = Get-Content -LiteralPath $file
        for ($lineIndex = 0; $lineIndex -lt $lines.Count; $lineIndex++) {
            $line = $lines[$lineIndex]
            foreach ($match in [regex]::Matches($line, '\[[^\]]+\]\(([^)]+)\)')) {
                $target = $match.Groups[1].Value.Trim()
                if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:' -or $target.StartsWith('#')) {
                    continue
                }

                $resolved = Resolve-RepoPath -FromFile $file -Target $target
                if ($null -eq $resolved) {
                    continue
                }

                if (-not (Test-Path -LiteralPath (Join-Path $repoRoot $resolved))) {
                    $markdownLinkProblems += [pscustomobject]@{
                        file = $file
                        line = $lineIndex + 1
                        target = $target
                        resolved = $resolved
                    }
                }
            }

            foreach ($match in [regex]::Matches($line, '`([^`]+)`')) {
                $target = $match.Groups[1].Value.Trim()
                if ($target -match '\s') {
                    continue
                }
                if ($target -notmatch '[/\\]' -and $target -notmatch '\.(md|png|json|txt|exe|ps1)$') {
                    continue
                }
                if ($target -match '^[a-zA-Z][a-zA-Z0-9+.-]*:') {
                    continue
                }

                $resolved = Resolve-RepoPath -FromFile $file -Target $target
                if ($null -eq $resolved) {
                    continue
                }

                $hasInvalidPathChars = $resolved -match '[*?<>|]'
                $exists = $false
                $isTracked = $false
                if (-not $hasInvalidPathChars) {
                    $exists = Test-Path -LiteralPath (Join-Path $repoRoot $resolved)
                    $isTracked = $tracked.Contains($resolved)
                }
                $category = Get-BacktickCategory -Path $target -Resolved $resolved -Exists $exists -IsTracked $isTracked

                $backtickPaths += [pscustomobject]@{
                    file = $file
                    line = $lineIndex + 1
                    target = $target
                    resolved = $resolved
                    exists = $exists
                    tracked = $isTracked
                    category = $category
                }
            }
        }
    }

    $actionableBacktickProblems = @(
        $backtickPaths | Where-Object { $_.category -eq 'unknown-missing' }
    )
    $categoryCounts = @{}
    foreach ($group in ($backtickPaths | Group-Object category)) {
        $categoryCounts[$group.Name] = $group.Count
    }

    $result = [pscustomobject]@{
        markdownFileCount = $markdownFiles.Count
        markdownLinkProblemCount = $markdownLinkProblems.Count
        backtickPathCount = $backtickPaths.Count
        actionableBacktickProblemCount = $actionableBacktickProblems.Count
        categoryCounts = $categoryCounts
        markdownLinkProblems = $markdownLinkProblems
        actionableBacktickProblems = $actionableBacktickProblems
    }

    if ($Json) {
        $result | ConvertTo-Json -Depth 8
    }
    else {
        $result
    }
}
finally {
    Pop-Location
}
