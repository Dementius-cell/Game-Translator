[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$ReleaseDirectory
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$releasePath = (Resolve-Path $ReleaseDirectory).Path
$detectorDirectory = Join-Path $releasePath "app\candidate-detector"
$sitePackages = Join-Path $detectorDirectory "Lib\site-packages"
$modelPath = Join-Path $detectorDirectory "paddlex-cache\official_models\PP-OCRv6_medium_det"
$verificationPath = Join-Path $releasePath "candidate-detector-headless-verification.json"
$portableOcrSmokePath = Join-Path $releasePath "portable-tesseract-ocr-smoke.json"
$tessdataPath = Join-Path $releasePath "app\tessdata"
$runtimeTarget = if (Test-Path -LiteralPath (Join-Path $releasePath "app\hostfxr.dll") -PathType Leaf) {
    "win-x64 self-contained .NET 9 desktop release candidate"
}
else {
    "win-x64 framework-dependent .NET 9 desktop release candidate"
}

foreach ($requiredPath in @($sitePackages, $modelPath, (Join-Path $detectorDirectory "python.exe"))) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Cannot finalize incomplete release candidate; required path is missing: $requiredPath"
    }
}

function Get-MetadataValue {
    param(
        [Parameter(Mandatory)] [AllowEmptyCollection()] [AllowEmptyString()] [string[]]$Lines,
        [Parameter(Mandatory)] [string]$Name
    )

    $prefix = "$Name`:"
    $line = $Lines | Where-Object { $_.StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase) } | Select-Object -First 1
    if ($null -eq $line) {
        return ""
    }

    return $line.Substring($prefix.Length).Trim()
}

$distributions = foreach ($metadata in Get-ChildItem -LiteralPath $sitePackages -Recurse -File -Filter "METADATA") {
    $lines = @(Get-Content -LiteralPath $metadata.FullName)
    [pscustomobject]@{
        Name = Get-MetadataValue -Lines $lines -Name "Name"
        Version = Get-MetadataValue -Lines $lines -Name "Version"
        License = Get-MetadataValue -Lines $lines -Name "License-Expression"
        MetadataPath = $metadata.FullName.Substring($detectorDirectory.Length + 1).Replace('\', '/')
    }
}

$bundledTesseractLanguagePacks = if (Test-Path -LiteralPath $tessdataPath -PathType Container) {
    @(
        Get-ChildItem -LiteralPath $tessdataPath -File -Filter "*.traineddata" |
            Sort-Object Name |
            ForEach-Object {
                [ordered]@{
                    code = [System.IO.Path]::GetFileNameWithoutExtension($_.Name)
                    relativePath = $_.FullName.Substring((Join-Path $releasePath "app").Length + 1).Replace('\', '/')
                    sha256 = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
                    sizeBytes = $_.Length
                    source = "bundled in release candidate"
                }
            }
    )
}
else {
    @()
}

$verification = if (Test-Path -LiteralPath $verificationPath) {
    Get-Content -LiteralPath $verificationPath -Raw | ConvertFrom-Json
}

if (-not (Test-Path -LiteralPath $portableOcrSmokePath -PathType Leaf)) {
    throw "Cannot finalize release candidate without packaged Tesseract OCR smoke evidence: $portableOcrSmokePath"
}
$portableOcrSmoke = Get-Content -LiteralPath $portableOcrSmokePath -Raw | ConvertFrom-Json
if ($portableOcrSmoke.status -ne "passed") {
    throw "Cannot finalize release candidate because packaged Tesseract OCR smoke did not pass."
}
else {
    $null
}

$manifest = [ordered]@{
    releaseName = Split-Path -Leaf $releasePath
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    target = $runtimeTarget
    candidatePipeline = "ADR-030 default detector-to-Tesseract pipeline; legacy full-page orchestration is not an automatic fallback"
    headlessVerification = $verification
    portableTesseractOcrSmoke = $portableOcrSmoke
    model = [ordered]@{
        name = "PP-OCRv6_medium_det"
        relativePath = "candidate-detector/paddlex-cache/official_models/PP-OCRv6_medium_det"
    }
    tesseractLanguagePacks = $bundledTesseractLanguagePacks
    distributions = $distributions | Sort-Object Name
}
$manifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath (Join-Path $releasePath "candidate-detector-runtime-manifest.json") -Encoding utf8

$notices = @(
    "# Third-Party Notices",
    "",
    "This local release candidate bundles CPython, PaddlePaddle GPU, PaddleOCR, PaddleX, PP-OCRv6_medium_det model files, CUDA dependency wheels, and their Python dependencies.",
    "",
    "- CPython is distributed under the Python Software Foundation License; its license text is included as candidate-detector/LICENSE.txt.",
    "- PaddlePaddle and PaddleOCR are Apache-2.0 upstream projects. See https://github.com/PaddlePaddle/Paddle and https://github.com/PaddlePaddle/PaddleOCR.",
    "- PP-OCRv6_medium_det is obtained from the PaddleOCR official model distribution. Its package path and hashes are recorded in the runtime manifest and SHA256SUMS.txt.",
    "- Wheel metadata and bundled package license texts remain under candidate-detector/Lib/site-packages/*.dist-info.",
    "",
    "Distribution inventory:"
)
if ($bundledTesseractLanguagePacks.Count -gt 0) {
    $notices += "- Tesseract tessdata_fast language data: $($bundledTesseractLanguagePacks.code -join ', ') (Apache-2.0; source https://github.com/tesseract-ocr/tessdata_fast; per-file SHA-256 values are recorded in candidate-detector-runtime-manifest.json and SHA256SUMS.txt)."
}
$notices += $distributions | Sort-Object Name | ForEach-Object { "- $($_.Name) $($_.Version) [$($_.License)] ($($_.MetadataPath))" }
$notices | Set-Content -LiteralPath (Join-Path $releasePath "THIRD-PARTY-NOTICES.md") -Encoding utf8

@(
    "# Candidate Detector Rollback",
    "",
    "This ADR-030 release candidate uses the packaged detector-to-Tesseract chain as its default product path. Existing profiles and saved zones remain compatible.",
    "",
    "To roll back this candidate, reinstall the preceding verified release. Do not remove candidate-detector in place as a substitute: the current default pipeline will correctly report a degraded state and emit no detector-derived overlay. No profile migration or data rollback is required.",
    "",
    "Do not delete or modify another release directory while rolling back this candidate."
) | Set-Content -LiteralPath (Join-Path $releasePath "ROLLBACK.md") -Encoding utf8

@(
    "# ADR-030 Default Candidate-Pipeline Release Candidate",
    "",
    "This artifact was assembled from a dirty local worktree. It is not a signed production release, but its default path is the ADR-030 detector-to-Tesseract pipeline.",
    "",
    "It contains the bundled GPU detector runtime required by the default candidate pipeline and $($runtimeTarget.ToLowerInvariant()). Record package integrity, offline-install, rollback and release-approval evidence before distribution."
) | Set-Content -LiteralPath (Join-Path $releasePath "RELEASE-CANDIDATE-NOTICE.md") -Encoding utf8

$hashes = Get-ChildItem -LiteralPath $releasePath -Recurse -File |
    Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
    Sort-Object FullName |
    ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $relative = $_.FullName.Substring($releasePath.Length + 1).Replace('\', '/')
        "$hash  $relative"
    }
$hashes | Set-Content -LiteralPath (Join-Path $releasePath "SHA256SUMS.txt") -Encoding ascii

Write-Host "Track D release candidate metadata finalized at: $releasePath"
