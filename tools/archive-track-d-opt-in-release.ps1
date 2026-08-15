[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$ReleaseDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$releasePath = (Resolve-Path $ReleaseDirectory).Path
$releaseName = Split-Path -Leaf $releasePath
$appDirectory = Join-Path $releasePath "app"
$archiveName = "GameTranslator-$releaseName-win-x64.zip"
$archivePath = Join-Path $releasePath $archiveName
$sumsPath = Join-Path $releasePath "SHA256SUMS.txt"

if (-not (Test-Path -LiteralPath $appDirectory -PathType Container)) {
    throw "Release candidate app directory is missing: $appDirectory"
}
if (-not (Test-Path -LiteralPath $sumsPath -PathType Leaf)) {
    throw "Finalize the release candidate before creating its archive: $sumsPath"
}
if (Test-Path -LiteralPath $archivePath) {
    throw "Release archive already exists and will not be overwritten: $archivePath"
}

& tar.exe -c --format=zip -f $archivePath -C $releasePath app
if ($LASTEXITCODE -ne 0) {
    throw "bsdtar failed to create the release archive with exit code $LASTEXITCODE."
}

$entries = & tar.exe -t -f $archivePath
if ($LASTEXITCODE -ne 0) {
    throw "bsdtar could not verify the release archive with exit code $LASTEXITCODE."
}
foreach ($requiredEntry in @(
    "app/GameTranslator.UI.exe",
    "app/GameTranslator.Infrastructure.dll",
    "app/candidate-detector/python.exe",
    "app/candidate-detector/paddle_text_detector_worker.py"
)) {
    if ($entries -notcontains $requiredEntry) {
        throw "Release archive is missing required entry: $requiredEntry"
    }
}

$archiveHash = (Get-FileHash -LiteralPath $archivePath -Algorithm SHA256).Hash.ToLowerInvariant()
Add-Content -LiteralPath $sumsPath -Value "$archiveHash  $archiveName" -Encoding ascii

[pscustomobject]@{
    Archive = $archivePath
    SizeMiB = [math]::Round((Get-Item -LiteralPath $archivePath).Length / 1MB, 1)
    EntryCount = @($entries).Count
    Sha256 = $archiveHash
} | ConvertTo-Json
