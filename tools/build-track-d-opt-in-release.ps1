[CmdletBinding()]
param(
    [Parameter()]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$PythonRuntimeRoot,

    [Parameter()]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$PaddleVenvRoot,

    [Parameter()]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$ModelCacheRoot,

    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$BootstrapRuntimeRoot,

    [string]$ReleaseName = "v0.1.0-pre.20260802-track-d-adr025",

    [string]$ReleaseRoot = (Join-Path $PSScriptRoot "..\artifacts\releases"),

    [ValidatePattern("^[a-z0-9_]+$")]
    [string[]]$TesseractLanguagePacks = @(),

    [switch]$ValidateRuntimeOnly,

    [switch]$SelfContained,

    [switch]$PruneRuntimePayload
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$bootstrapRuntimeManifest = $null
$usesBootstrapRuntime = -not [string]::IsNullOrWhiteSpace($BootstrapRuntimeRoot)
$explicitRuntimeInputs = @($PythonRuntimeRoot, $PaddleVenvRoot, $ModelCacheRoot)
if ($usesBootstrapRuntime) {
    if ($explicitRuntimeInputs | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }) {
        throw "Specify either -BootstrapRuntimeRoot or the explicit Python/Paddle/model roots, not both."
    }

    $BootstrapRuntimeRoot = (Resolve-Path $BootstrapRuntimeRoot).Path
    $bootstrapVerifier = Join-Path $PSScriptRoot "verify-paddle-runtime.ps1"
    & $bootstrapVerifier -RuntimeRoot $BootstrapRuntimeRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned Paddle runtime verification failed with exit code $LASTEXITCODE."
    }

    $PythonRuntimeRoot = Join-Path $BootstrapRuntimeRoot "python"
    $PaddleVenvRoot = Join-Path $BootstrapRuntimeRoot "venv"
    $ModelCacheRoot = Join-Path $BootstrapRuntimeRoot "models"
    $bootstrapRuntimeManifest = Get-Content -LiteralPath (Join-Path $BootstrapRuntimeRoot "paddle-runtime-manifest.json") -Raw | ConvertFrom-Json
}
elseif ($explicitRuntimeInputs | Where-Object { [string]::IsNullOrWhiteSpace($_) }) {
    throw "Specify -BootstrapRuntimeRoot or all of -PythonRuntimeRoot, -PaddleVenvRoot and -ModelCacheRoot."
}

if ($ValidateRuntimeOnly) {
    if (-not $usesBootstrapRuntime) {
        throw "-ValidateRuntimeOnly requires -BootstrapRuntimeRoot."
    }

    Write-Host "Pinned Paddle runtime is valid for packaging: $BootstrapRuntimeRoot"
    $global:LASTEXITCODE = 0
    return
}

$uiProject = Join-Path $repositoryRoot "src\GameTranslator.UI\GameTranslator.UI.csproj"
$releaseRootPath = [System.IO.Path]::GetFullPath($ReleaseRoot)
$releaseDirectory = Join-Path $releaseRootPath $ReleaseName
$appDirectory = Join-Path $releaseDirectory "app"
$runtimeTarget = if ($SelfContained) {
    "win-x64 self-contained .NET 9 desktop release candidate"
}
else {
    "win-x64 framework-dependent .NET 9 desktop release candidate"
}
$detectorDirectory = Join-Path $appDirectory "candidate-detector"
$venvSitePackages = Join-Path $PaddleVenvRoot "Lib\site-packages"
$detectorSitePackages = Join-Path $detectorDirectory "Lib\site-packages"
$tessdataSourceDirectory = Join-Path $repositoryRoot "tessdata"
$packagedTessdataDirectory = Join-Path $appDirectory "tessdata"
$portableOcrSmokeReportPath = Join-Path $releaseDirectory "portable-tesseract-ocr-smoke.json"
$modelDirectoryName = "PP-OCRv6_medium_det"
$modelSourceDirectory = Join-Path $ModelCacheRoot "official_models\$modelDirectoryName"
$runtimePruning = [ordered]@{
    enabled = [bool]$PruneRuntimePayload
    excludedBaseRuntimeDirectories = @()
    excludedSitePackageDirectoryNames = @()
    excludedSitePackageFilePatterns = @()
}

function Invoke-Robocopy {
    param(
        [Parameter(Mandatory)] [string]$Source,
        [Parameter(Mandatory)] [string]$Destination,
        [Parameter(Mandatory)] [string[]]$Arguments
    )

    & robocopy $Source $Destination @Arguments | Out-Host
    $robocopyExitCode = $LASTEXITCODE
    if ($robocopyExitCode -gt 7) {
        throw "robocopy failed from '$Source' to '$Destination' with exit code $robocopyExitCode."
    }

    # Robocopy uses 1-7 for successful copy outcomes. Normalize those so a
    # successful release build does not report a false non-zero exit code.
    $global:LASTEXITCODE = 0
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

function Assert-SelfContainedWindowsDesktopRuntime {
    param(
        [Parameter(Mandatory)] [string]$ApplicationDirectory
    )

    $runtimeConfigPath = Join-Path $ApplicationDirectory "GameTranslator.UI.runtimeconfig.json"
    if (-not (Test-Path -LiteralPath $runtimeConfigPath -PathType Leaf)) {
        throw "Self-contained publish is missing its runtime configuration: $runtimeConfigPath"
    }

    $runtimeConfig = Get-Content -LiteralPath $runtimeConfigPath -Raw | ConvertFrom-Json
    $windowsDesktopFramework = @($runtimeConfig.runtimeOptions.includedFrameworks | Where-Object { $_.name -eq "Microsoft.WindowsDesktop.App" }) | Select-Object -First 1
    if ($null -eq $windowsDesktopFramework) {
        throw "Self-contained publish does not declare Microsoft.WindowsDesktop.App."
    }

    $expectedMajorVersion = ([version]$windowsDesktopFramework.version).Major
    foreach ($assemblyFileName in @("WindowsBase.dll", "PresentationCore.dll", "PresentationFramework.dll", "System.Xaml.dll")) {
        $assemblyPath = Join-Path $ApplicationDirectory $assemblyFileName
        if (-not (Test-Path -LiteralPath $assemblyPath -PathType Leaf)) {
            throw "Self-contained publish is missing WindowsDesktop runtime assembly: $assemblyPath"
        }

        $assemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($assemblyPath)
        if ($assemblyName.Version.Major -ne $expectedMajorVersion) {
            throw "Self-contained publish contains an incompatible $assemblyFileName identity '$assemblyName' instead of WindowsDesktop major version $expectedMajorVersion."
        }
    }
}

function Assert-SelfContainedExternalModuleReferenceClosure {
    param(
        [Parameter(Mandatory)] [string]$ApplicationDirectory
    )

    $modulePath = Join-Path $ApplicationDirectory "GameTranslator.Infrastructure.dll"
    if (-not (Test-Path -LiteralPath $modulePath -PathType Leaf)) {
        throw "Self-contained publish is missing the external Infrastructure composition module: $modulePath"
    }

    $moduleAssembly = [System.Reflection.Assembly]::ReflectionOnlyLoadFrom($modulePath)
    foreach ($requiredAssembly in $moduleAssembly.GetReferencedAssemblies()) {
        $dependencyPath = Join-Path $ApplicationDirectory "$($requiredAssembly.Name).dll"
        if (-not (Test-Path -LiteralPath $dependencyPath -PathType Leaf)) {
            throw "Self-contained publish is missing Infrastructure dependency '$requiredAssembly': $dependencyPath"
        }

        $actualAssembly = [System.Reflection.AssemblyName]::GetAssemblyName($dependencyPath)
        if ($actualAssembly.FullName -ne $requiredAssembly.FullName) {
            throw "Self-contained publish has incompatible Infrastructure dependency '$actualAssembly' at '$dependencyPath'; expected '$requiredAssembly'."
        }
    }
}

function Assert-PackagedTesseractNativeLayout {
    param(
        [Parameter(Mandatory)] [string]$ApplicationDirectory
    )

    foreach ($libraryName in @("leptonica-1.85.0.dll", "tesseract55.dll")) {
        $expectedPath = Join-Path $ApplicationDirectory "x64\$libraryName"
        if (-not (Test-Path -LiteralPath $expectedPath -PathType Leaf)) {
            throw "Published app is missing the Tesseract x64 native library at its architecture-specific path: $expectedPath"
        }

        $flattenedPath = Join-Path $ApplicationDirectory $libraryName
        if (Test-Path -LiteralPath $flattenedPath -PathType Leaf) {
            throw "Published app contains a flattened Tesseract native library instead of the required x64 layout: $flattenedPath"
        }
    }
}

$normalizedTesseractLanguagePacks = @(
    $TesseractLanguagePacks |
        ForEach-Object { $_.Trim().ToLowerInvariant() } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
        Select-Object -Unique
)

if (Test-Path -LiteralPath $releaseDirectory) {
    throw "Release directory already exists and will not be overwritten: $releaseDirectory"
}

foreach ($requiredPath in @($uiProject, $venvSitePackages, $modelSourceDirectory, (Join-Path $PythonRuntimeRoot "python.exe"))) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required packaging input is missing: $requiredPath"
    }
}

if ($normalizedTesseractLanguagePacks.Count -gt 0 -and -not (Test-Path -LiteralPath $tessdataSourceDirectory -PathType Container)) {
    throw "Requested Tesseract language packs, but the local source directory is missing: $tessdataSourceDirectory"
}

foreach ($language in $normalizedTesseractLanguagePacks) {
    $sourcePath = Join-Path $tessdataSourceDirectory "$language.traineddata"
    if (-not (Test-Path -LiteralPath $sourcePath -PathType Leaf)) {
        throw "Requested Tesseract language pack is missing: $sourcePath"
    }
}

New-Item -ItemType Directory -Path $releaseDirectory | Out-Null

try {
    Push-Location $repositoryRoot
    & dotnet restore GameTranslator.sln -r win-x64
    if ($LASTEXITCODE -ne 0) {
        throw "RID-specific dotnet restore failed with exit code $LASTEXITCODE."
    }

    if ($SelfContained) {
        & dotnet publish $uiProject -c Release -r win-x64 --self-contained --no-restore -o $appDirectory
    }
    else {
        & dotnet publish $uiProject -c Release -r win-x64 --self-contained false --no-restore -o $appDirectory
    }
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed with exit code $LASTEXITCODE."
    }

    if ($SelfContained) {
        Assert-SelfContainedWindowsDesktopRuntime -ApplicationDirectory $appDirectory
        Assert-SelfContainedExternalModuleReferenceClosure -ApplicationDirectory $appDirectory
    }
    Assert-PackagedTesseractNativeLayout -ApplicationDirectory $appDirectory
}
finally {
    Pop-Location
}

$packagedTesseractLanguagePackRecords = @()
if ($normalizedTesseractLanguagePacks.Count -gt 0) {
    # The release directory is newly created above; make the requested pack list authoritative over publish leftovers.
    if (Test-Path -LiteralPath $packagedTessdataDirectory) {
        Remove-Item -LiteralPath $packagedTessdataDirectory -Recurse -Force
    }
    New-Item -ItemType Directory -Path $packagedTessdataDirectory -Force | Out-Null
    $packagedTesseractLanguagePackRecords = foreach ($language in $normalizedTesseractLanguagePacks) {
        $sourcePath = Join-Path $tessdataSourceDirectory "$language.traineddata"
        $targetPath = Join-Path $packagedTessdataDirectory "$language.traineddata"
        Copy-Item -LiteralPath $sourcePath -Destination $targetPath -Force
        [ordered]@{
            code = $language
            relativePath = "tessdata/$language.traineddata"
            sha256 = (Get-FileHash -LiteralPath $targetPath -Algorithm SHA256).Hash.ToLowerInvariant()
            sizeBytes = (Get-Item -LiteralPath $targetPath).Length
            source = "local repository tessdata/$language.traineddata"
        }
    }
}

if (-not ($normalizedTesseractLanguagePacks -contains "eng")) {
    throw "The mandatory packaged Tesseract OCR smoke requires the eng language pack."
}

$packagedExecutable = Join-Path $appDirectory "GameTranslator.UI.exe"
$smokeArguments = @(
    "--portable-ocr-smoke",
    "`"$portableOcrSmokeReportPath`""
)
$smokeProcess = Start-Process `
    -FilePath $packagedExecutable `
    -ArgumentList $smokeArguments `
    -WorkingDirectory $appDirectory `
    -WindowStyle Hidden `
    -Wait `
    -PassThru
if ($smokeProcess.ExitCode -ne 0) {
    $smokeFailure = if (Test-Path -LiteralPath $portableOcrSmokeReportPath -PathType Leaf) {
        Get-Content -LiteralPath $portableOcrSmokeReportPath -Raw
    }
    else {
        "The packaged executable did not write its smoke report."
    }
    throw "Packaged Tesseract OCR smoke failed with exit code $($smokeProcess.ExitCode): $smokeFailure"
}

if (-not (Test-Path -LiteralPath $portableOcrSmokeReportPath -PathType Leaf)) {
    throw "The packaged Tesseract OCR smoke did not write its report: $portableOcrSmokeReportPath"
}
$portableOcrSmokeResult = Get-Content -LiteralPath $portableOcrSmokeReportPath -Raw | ConvertFrom-Json
if ($portableOcrSmokeResult.status -ne "passed") {
    throw "The packaged Tesseract OCR smoke report does not declare a passed result."
}
$expectedSmokeBaseDirectory = [System.IO.Path]::GetFullPath($appDirectory).TrimEnd('\')
$actualSmokeBaseDirectory = [System.IO.Path]::GetFullPath([string]$portableOcrSmokeResult.baseDirectory).TrimEnd('\')
if (-not $actualSmokeBaseDirectory.Equals($expectedSmokeBaseDirectory, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The packaged Tesseract OCR smoke ran from '$actualSmokeBaseDirectory' instead of '$expectedSmokeBaseDirectory'."
}

# Serilog creates this directory because the packaged executable itself is the smoke host.
# It contains only synthetic-smoke startup lines and is not part of the candidate payload.
$packagedLogDirectory = [System.IO.Path]::GetFullPath((Join-Path $appDirectory "logs"))
if (-not $packagedLogDirectory.StartsWith(
        ([System.IO.Path]::GetFullPath($appDirectory).TrimEnd('\') + '\'),
        [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Refusing to clean an unexpected packaged smoke log directory: $packagedLogDirectory"
}
if (Test-Path -LiteralPath $packagedLogDirectory -PathType Container) {
    Remove-Item -LiteralPath $packagedLogDirectory -Recurse -Force
}

$workerScript = Join-Path $detectorDirectory "paddle_text_detector_worker.py"
if (-not (Test-Path -LiteralPath $workerScript)) {
    throw "Published app output does not contain the candidate detector worker: $workerScript"
}
$workerContent = Get-Content -LiteralPath $workerScript -Raw -Encoding utf8

# Publish can bring a stale candidate-detector payload from the Infrastructure output.
# Recreate this generated directory so the selected runtime inventory is authoritative.
Remove-Item -LiteralPath $detectorDirectory -Recurse -Force
New-Item -ItemType Directory -Path $detectorDirectory | Out-Null
Set-Content -LiteralPath $workerScript -Value $workerContent -Encoding utf8 -NoNewline

$baseRuntimeExclusions = @(
    Join-Path $PythonRuntimeRoot "Lib\site-packages"
)
$sitePackageCopyArguments = @(
    "/E", "/COPY:DAT", "/DCOPY:T", "/R:1", "/W:1", "/NP", "/NFL", "/NDL"
)
if ($PruneRuntimePayload) {
    $runtimePruning.excludedBaseRuntimeDirectories = @(
        "Doc",
        "include",
        "libs",
        "Scripts",
        "tcl",
        "Lib\test",
        "Lib\idlelib",
        "Lib\ensurepip",
        "Lib\turtledemo"
    )
    $runtimePruning.excludedSitePackageDirectoryNames = @("__pycache__", "test", "tests")
    $runtimePruning.excludedSitePackageFilePatterns = @("*.pyc", "*.pyo")
    $baseRuntimeExclusions += $runtimePruning.excludedBaseRuntimeDirectories |
        ForEach-Object { Join-Path $PythonRuntimeRoot $_ }
    $sitePackageCopyArguments += @("/XD") + $runtimePruning.excludedSitePackageDirectoryNames
    $sitePackageCopyArguments += @("/XF") + $runtimePruning.excludedSitePackageFilePatterns
}

$baseRuntimeCopyArguments = @(
    "/E", "/COPY:DAT", "/DCOPY:T", "/R:1", "/W:1", "/NP", "/NFL", "/NDL",
    "/XD"
) + $baseRuntimeExclusions
if ($PruneRuntimePayload) {
    $baseRuntimeCopyArguments += @("__pycache__", "/XF", "*.pyc", "*.pyo")
}
Invoke-Robocopy -Source $PythonRuntimeRoot -Destination $detectorDirectory -Arguments $baseRuntimeCopyArguments
Invoke-Robocopy -Source $venvSitePackages -Destination $detectorSitePackages -Arguments $sitePackageCopyArguments
Invoke-Robocopy -Source $modelSourceDirectory -Destination (Join-Path $detectorDirectory "paddlex-cache\official_models\$modelDirectoryName") -Arguments @(
    "/E", "/COPY:DAT", "/DCOPY:T", "/R:1", "/W:1", "/NP", "/NFL", "/NDL"
)

$packagedPython = Join-Path $detectorDirectory "python.exe"
$probePath = Join-Path $detectorDirectory ".packaging-import-probe.py"
$probeErrorPath = Join-Path $detectorDirectory ".packaging-import-probe.stderr.log"
$probe = @'
import json
import pathlib
import sys

import paddle
import paddleocr

print(json.dumps({
    "pythonVersion": sys.version.split()[0],
    "prefix": str(pathlib.Path(sys.prefix).resolve()),
    "paddleVersion": paddle.__version__,
    "paddleOcrVersion": paddleocr.__version__,
    "cudaAvailable": paddle.device.is_compiled_with_cuda(),
    "device": paddle.device.get_device(),
}, separators=(",", ":")))
'@
Set-Content -LiteralPath $probePath -Value $probe -Encoding utf8
try {
    $previousErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    try {
        $probeArguments = if ($PruneRuntimePayload) { @("-I", "-B", $probePath) } else { @("-I", $probePath) }
        $probeOutput = & $packagedPython @probeArguments 2> $probeErrorPath
    }
    finally {
        $ErrorActionPreference = $previousErrorActionPreference
    }

    if ($LASTEXITCODE -ne 0) {
        $probeError = if (Test-Path -LiteralPath $probeErrorPath) { Get-Content -LiteralPath $probeErrorPath -Raw } else { "" }
        throw "The packaged Python runtime could not import PaddleOCR: $probeError"
    }
}
finally {
    Remove-Item -LiteralPath $probePath -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath $probeErrorPath -Force -ErrorAction SilentlyContinue
}

$probeJson = $probeOutput | Where-Object { $_ -match '^\{.*\}$' } | Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($probeJson)) {
    throw "The packaged Python runtime did not return its import probe JSON."
}
$probeResult = $probeJson | ConvertFrom-Json
$expectedPrefix = [System.IO.Path]::GetFullPath($detectorDirectory).TrimEnd('\')
if (-not $probeResult.prefix.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "The packaged runtime resolved sys.prefix outside the release candidate: $($probeResult.prefix)"
}

$distributions = foreach ($metadata in Get-ChildItem -LiteralPath $detectorSitePackages -Recurse -File -Filter "METADATA") {
    $lines = Get-Content -LiteralPath $metadata.FullName
    [pscustomobject]@{
        Name = Get-MetadataValue -Lines $lines -Name "Name"
        Version = Get-MetadataValue -Lines $lines -Name "Version"
        License = Get-MetadataValue -Lines $lines -Name "License-Expression"
        MetadataPath = $metadata.FullName.Substring($detectorDirectory.Length + 1).Replace('\', '/')
    }
}

$manifest = [ordered]@{
    releaseName = $ReleaseName
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    target = $runtimeTarget
    candidatePipeline = "ADR-030 default detector-to-Tesseract pipeline; legacy full-page orchestration is not an automatic fallback"
    packagedPython = $probeResult
    model = [ordered]@{
        name = "PP-OCRv6_medium_det"
        relativePath = "candidate-detector/paddlex-cache/official_models/PP-OCRv6_medium_det"
    }
    bootstrapRuntime = if ($null -eq $bootstrapRuntimeManifest) { $null } else {
        [ordered]@{
            runtimeId = $bootstrapRuntimeManifest.runtimeId
            generatedAtUtc = $bootstrapRuntimeManifest.generatedAtUtc
            python = $bootstrapRuntimeManifest.python
            packages = $bootstrapRuntimeManifest.packages
            model = $bootstrapRuntimeManifest.model
        }
    }
    tesseractLanguagePacks = $packagedTesseractLanguagePackRecords
    portableTesseractOcrSmoke = $portableOcrSmokeResult
    runtimePruning = $runtimePruning
    distributions = $distributions | Sort-Object Name
}
$manifest | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $releaseDirectory "candidate-detector-runtime-manifest.json") -Encoding utf8

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
if ($packagedTesseractLanguagePackRecords.Count -gt 0) {
    $notices += "- Tesseract tessdata_fast language data: $($packagedTesseractLanguagePackRecords.code -join ', ') (Apache-2.0; source https://github.com/tesseract-ocr/tessdata_fast; per-file SHA-256 values are recorded in candidate-detector-runtime-manifest.json and SHA256SUMS.txt)."
}
$notices += $distributions | Sort-Object Name | ForEach-Object { "- $($_.Name) $($_.Version) [$($_.License)] ($($_.MetadataPath))" }
$notices | Set-Content -LiteralPath (Join-Path $releaseDirectory "THIRD-PARTY-NOTICES.md") -Encoding utf8

$rollback = @(
    "# Candidate Detector Rollback",
    "",
    "This ADR-030 release candidate uses the packaged detector-to-Tesseract chain as its default product path. Existing profiles and saved zones remain compatible.",
    "",
    "To roll back this candidate, reinstall the preceding verified release. Do not remove candidate-detector in place as a substitute: the current default pipeline will correctly report a degraded state and emit no detector-derived overlay. No profile migration or data rollback is required.",
    "",
    "Do not delete or modify another release directory while rolling back this candidate."
)
$rollback | Set-Content -LiteralPath (Join-Path $releaseDirectory "ROLLBACK.md") -Encoding utf8

$notice = @(
    "# ADR-030 Default Candidate-Pipeline Release Candidate",
    "",
    "This artifact was assembled from a dirty local worktree. It is not a signed production release, but its default path is the ADR-030 detector-to-Tesseract pipeline.",
    "",
    "It contains the bundled GPU detector runtime required by the default candidate pipeline and $($runtimeTarget.ToLowerInvariant()). Record package integrity, offline-install, rollback and release-approval evidence before distribution."
)
$notice | Set-Content -LiteralPath (Join-Path $releaseDirectory "RELEASE-CANDIDATE-NOTICE.md") -Encoding utf8

$hashes = Get-ChildItem -LiteralPath $releaseDirectory -Recurse -File |
    Where-Object { $_.Name -ne "SHA256SUMS.txt" } |
    Sort-Object FullName |
    ForEach-Object {
        $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        $relative = $_.FullName.Substring($releaseDirectory.Length + 1).Replace('\', '/')
        "$hash  $relative"
    }
$hashes | Set-Content -LiteralPath (Join-Path $releaseDirectory "SHA256SUMS.txt") -Encoding ascii

Write-Host "ADR-030 default candidate-pipeline release candidate assembled at: $releaseDirectory"
Write-Host "Run tools/verify-track-d-opt-in-release.ps1 against this directory before treating it as packaging evidence."
$global:LASTEXITCODE = 0
