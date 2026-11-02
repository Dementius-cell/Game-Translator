param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))
$ErrorActionPreference = 'Stop'
Push-Location -LiteralPath $RepositoryRoot
try {
    $paths = @(git -c core.quotePath=false diff --cached --name-only --diff-filter=ACMR)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect staged files.' }
    $newPaths = @(git -c core.quotePath=false diff --cached --name-only --diff-filter=A)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect added files.' }
    $blocked = @($paths | Where-Object {
        $_ -match '(^|/)(work|outputs|runtime|models|venv|\.venv|tessdata|logs|bin|obj)/' -or
        $_ -match '^artifacts/(releases|manual[^/]*)/' -or
        $_ -in @('docs/agents/current-development-handoff-2026-10-31.md', 'docs/agents/reinstall-handoff-2026-09-07.md')
    })
    # Existing approved fixtures remain valid; new evidence requires an explicit review.
    $blocked += @($newPaths | Where-Object { $_ -match '^artifacts/' -or $_ -match '\.(exe|dll|pdb|etl|zip|7z|png|jpe?g|webp|bmp|trx)$' })
    if ($blocked.Count) { throw ('Local-only or unreviewed artifacts staged: ' + (($blocked | Sort-Object -Unique) -join ', ')) }
    git diff --cached --check
    if ($LASTEXITCODE -ne 0) { throw 'Staged whitespace validation failed.' }
    Write-Output 'Publication paths and staged whitespace passed. Review content separately for private text and secrets.'
}
finally { Pop-Location }
