[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$PythonRuntimeRoot,

    [string]$RuntimeRoot = (Join-Path $PSScriptRoot "..\work\paddle-runtime-win-x64"),

    [switch]$SkipGpuProbe
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$lockPath = Join-Path $PSScriptRoot "paddle-runtime\paddle-runtime-win-x64.lock.json"
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$requirementsPath = Join-Path (Split-Path -Parent $lockPath) $lock.pythonPackages.requirementsPath
$sourcePython = Join-Path (Resolve-Path $PythonRuntimeRoot).Path "python.exe"
$runtimePath = [System.IO.Path]::GetFullPath($RuntimeRoot)
$packagedPythonRoot = Join-Path $runtimePath "python"
$venvRoot = Join-Path $runtimePath "venv"
$modelCacheRoot = Join-Path $runtimePath "models"
$modelRoot = Join-Path $modelCacheRoot "official_models\$($lock.model.name)"
$tessdataRoot = Join-Path $repositoryRoot "tessdata"
$runtimeManifestPath = Join-Path $runtimePath "paddle-runtime-manifest.json"

function Invoke-External {
    param(
        [Parameter(Mandatory)] [string]$FilePath,
        [Parameter(Mandatory)] [string[]]$Arguments,
        [Parameter(Mandatory)] [string]$Description
    )

    & $FilePath @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Description failed with exit code $LASTEXITCODE."
    }
}

function Get-Sha256 {
    param([Parameter(Mandatory)] [string]$Path)

    return (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash.ToLowerInvariant()
}

function Assert-Sha256 {
    param(
        [Parameter(Mandatory)] [string]$Path,
        [Parameter(Mandatory)] [string]$ExpectedHash,
        [Parameter(Mandatory)] [string]$Description
    )

    $actualHash = Get-Sha256 -Path $Path
    if (-not [string]::Equals($actualHash, $ExpectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description hash mismatch. Expected ${ExpectedHash}, received ${actualHash}: $Path"
    }
}

function Get-RemoteFile {
    param(
        [Parameter(Mandatory)] [string]$Uri,
        [Parameter(Mandatory)] [string]$DestinationPath,
        [Parameter(Mandatory)] [string]$ExpectedHash,
        [Parameter(Mandatory)] [string]$Description
    )

    if (Test-Path -LiteralPath $DestinationPath -PathType Leaf) {
        Assert-Sha256 -Path $DestinationPath -ExpectedHash $ExpectedHash -Description $Description
        return
    }

    $parent = Split-Path -Parent $DestinationPath
    New-Item -ItemType Directory -Path $parent -Force | Out-Null
    $temporaryPath = "$DestinationPath.download"
    if (Test-Path -LiteralPath $temporaryPath) {
        Remove-Item -LiteralPath $temporaryPath -Force
    }

    try {
        Invoke-WebRequest -Uri $Uri -OutFile $temporaryPath
        Assert-Sha256 -Path $temporaryPath -ExpectedHash $ExpectedHash -Description $Description
        Move-Item -LiteralPath $temporaryPath -Destination $DestinationPath
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -Force
        }
    }
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

    # Robocopy reserves 1-7 for successful copy outcomes. Do not leak one of
    # those success codes as the exit code of the bootstrap script.
    $global:LASTEXITCODE = 0
}

if (-not (Test-Path -LiteralPath $sourcePython -PathType Leaf)) {
    throw "CPython executable was not found: $sourcePython"
}

$pythonVersion = (& $sourcePython -I -B -c "import sys; print(sys.version.split()[0])").Trim()
if (-not [string]::Equals($pythonVersion, $lock.python.version, [System.StringComparison]::Ordinal)) {
    throw "CPython $($lock.python.version) is required, but '$sourcePython' is $pythonVersion. Download the exact official CPython release listed in $lockPath."
}

New-Item -ItemType Directory -Path $runtimePath -Force | Out-Null

if (-not (Test-Path -LiteralPath $venvRoot -PathType Container)) {
    Invoke-External -FilePath $sourcePython -Arguments @("-m", "venv", $venvRoot) -Description "CPython virtual-environment creation"
}

$venvPython = Join-Path $venvRoot "Scripts\python.exe"
if (-not (Test-Path -LiteralPath $venvPython -PathType Leaf)) {
    throw "Bootstrap virtual-environment interpreter is missing: $venvPython"
}

Invoke-External -FilePath $venvPython -Arguments @(
    "-m", "pip", "install", "--upgrade", "--only-binary=:all:",
    "--index-url", "https://pypi.org/simple",
    "--extra-index-url", $lock.pythonPackages.paddlePackageIndex,
    "--requirement", $requirementsPath
) -Description "Pinned Paddle runtime installation"

$packageProbe = @'
import json
import cv2
import numpy
import paddle
import paddleocr
import paddlex

print(json.dumps({
    "paddle": paddle.__version__,
    "paddleocr": paddleocr.__version__,
    "paddlex": paddlex.__version__,
    "cudaAvailable": paddle.device.is_compiled_with_cuda(),
    "device": paddle.device.get_device(),
}, separators=(",", ":")))
'@
$packageProbePath = Join-Path $runtimePath ".paddle-runtime-probe.py"
Set-Content -LiteralPath $packageProbePath -Value $packageProbe -Encoding utf8 -NoNewline
try {
    $probeOutput = & $venvPython -I -B $packageProbePath
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned Paddle runtime probe failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -LiteralPath $packageProbePath -Force -ErrorAction SilentlyContinue
}

$probeLine = $probeOutput | Where-Object { $_ -match '^\{.*\}$' } | Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($probeLine)) {
    throw "Pinned Paddle runtime probe did not return JSON."
}
$probe = $probeLine | ConvertFrom-Json
foreach ($expectedPackage in @(
    @{ Name = "PaddlePaddle GPU"; Expected = $lock.pythonPackages.paddlepaddleGpu; Actual = $probe.paddle },
    @{ Name = "PaddleOCR"; Expected = $lock.pythonPackages.paddleOcr; Actual = $probe.paddleocr },
    @{ Name = "PaddleX"; Expected = $lock.pythonPackages.paddleX; Actual = $probe.paddlex }
)) {
    if (-not [string]::Equals([string]$expectedPackage.Actual, [string]$expectedPackage.Expected, [System.StringComparison]::Ordinal)) {
        throw "$($expectedPackage.Name) version mismatch. Expected $($expectedPackage.Expected), received $($expectedPackage.Actual)."
    }
}
if (-not $SkipGpuProbe -and (-not [bool]$probe.cudaAvailable -or -not ([string]$probe.device).StartsWith("gpu", [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "The pinned runtime did not expose a CUDA GPU. Device='$($probe.device)'; CUDA available='$($probe.cudaAvailable)'."
}

foreach ($modelFile in $lock.model.files) {
    $relativePath = [string]$modelFile.path
    $destinationPath = Join-Path $modelRoot $relativePath
    $uri = "https://huggingface.co/PaddlePaddle/$($lock.model.name)/resolve/$($lock.model.revision)/$relativePath"
    Get-RemoteFile -Uri $uri -DestinationPath $destinationPath -ExpectedHash ([string]$modelFile.sha256) -Description "Paddle model '$relativePath'"
}

$modelProbe = @'
import json
import sys

import paddle

model_root = sys.argv[1]
config = paddle.inference.Config(
    f"{model_root}/inference.json",
    f"{model_root}/inference.pdiparams",
)
config.disable_glog_info()
config.enable_use_gpu(1024, 0)
config.switch_ir_optim(True)
predictor = paddle.inference.create_predictor(config)
print(json.dumps({
    "inputCount": len(predictor.get_input_names()),
    "outputCount": len(predictor.get_output_names()),
    "device": paddle.device.get_device(),
}, separators=(",", ":")))
'@
$modelProbePath = Join-Path $runtimePath ".paddle-model-probe.py"
Set-Content -LiteralPath $modelProbePath -Value $modelProbe -Encoding utf8 -NoNewline
try {
    $modelProbeOutput = & $venvPython -I -B $modelProbePath $modelRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned PP-OCRv6 model probe failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -LiteralPath $modelProbePath -Force -ErrorAction SilentlyContinue
}
$modelProbeLine = $modelProbeOutput | Where-Object { $_ -match '^\{.*\}$' } | Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($modelProbeLine)) {
    throw "Pinned PP-OCRv6 model probe did not return JSON."
}
$modelProbeResult = $modelProbeLine | ConvertFrom-Json
if ($modelProbeResult.inputCount -lt 1 -or $modelProbeResult.outputCount -lt 1) {
    throw "Pinned PP-OCRv6 model probe returned an invalid predictor interface."
}

foreach ($languagePack in $lock.tesseractLanguagePacks) {
    $destinationPath = Join-Path $tessdataRoot "$($languagePack.code).traineddata"
    Get-RemoteFile -Uri ([string]$languagePack.source) -DestinationPath $destinationPath -ExpectedHash ([string]$languagePack.sha256) -Description "Tesseract language pack '$($languagePack.code)'"
}

if (-not (Test-Path -LiteralPath $packagedPythonRoot -PathType Container)) {
    Invoke-Robocopy -Source $PythonRuntimeRoot -Destination $packagedPythonRoot -Arguments @(
        "/E", "/COPY:DAT", "/DCOPY:T", "/R:1", "/W:1", "/NP", "/NFL", "/NDL",
        "/XD", (Join-Path $PythonRuntimeRoot "Lib\site-packages")
    )
}

$runtimeManifest = [ordered]@{
    schemaVersion = $lock.schemaVersion
    runtimeId = $lock.runtimeId
    generatedAtUtc = [DateTimeOffset]::UtcNow.ToString("O")
    platform = $lock.platform
    python = [ordered]@{
        version = $pythonVersion
        runtimeRoot = $packagedPythonRoot
    }
    virtualEnvironmentRoot = $venvRoot
    modelCacheRoot = $modelCacheRoot
    model = [ordered]@{
        name = $lock.model.name
        revision = $lock.model.revision
        files = @(
            $lock.model.files | ForEach-Object {
                [ordered]@{
                    path = $_.path
                    sha256 = $_.sha256
                }
            }
        )
    }
    tesseractRoot = $tessdataRoot
    tesseractLanguagePacks = @(
        $lock.tesseractLanguagePacks | ForEach-Object {
            [ordered]@{
                code = $_.code
                sha256 = $_.sha256
            }
        }
    )
    packages = [ordered]@{
        paddlepaddleGpu = $probe.paddle
        paddleOcr = $probe.paddleocr
        paddleX = $probe.paddlex
    }
    gpuProbe = [ordered]@{
        cudaAvailable = [bool]$probe.cudaAvailable
        device = $probe.device
        skipped = [bool]$SkipGpuProbe
    }
    modelProbe = [ordered]@{
        inputCount = $modelProbeResult.inputCount
        outputCount = $modelProbeResult.outputCount
        device = $modelProbeResult.device
    }
}
$runtimeManifest | ConvertTo-Json -Depth 7 | Set-Content -LiteralPath $runtimeManifestPath -Encoding utf8

Write-Host "Pinned Paddle runtime bootstrap completed: $runtimePath"
Write-Host "Package with: .\tools\build-track-d-opt-in-release.ps1 -BootstrapRuntimeRoot '$runtimePath' -TesseractLanguagePacks eng,jpn,chi_sim,tha"
$global:LASTEXITCODE = 0
