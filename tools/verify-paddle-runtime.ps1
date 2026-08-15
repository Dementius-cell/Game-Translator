[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$RuntimeRoot,

    [switch]$SkipGpuProbe
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$repositoryRoot = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$lockPath = Join-Path $PSScriptRoot "paddle-runtime\paddle-runtime-win-x64.lock.json"
$lock = Get-Content -LiteralPath $lockPath -Raw | ConvertFrom-Json
$runtimePath = (Resolve-Path $RuntimeRoot).Path
$manifestPath = Join-Path $runtimePath "paddle-runtime-manifest.json"
$pythonRoot = Join-Path $runtimePath "python"
$venvRoot = Join-Path $runtimePath "venv"
$venvPython = Join-Path $venvRoot "Scripts\python.exe"
$modelRoot = Join-Path $runtimePath "models\official_models\$($lock.model.name)"
$tessdataRoot = Join-Path $repositoryRoot "tessdata"

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

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "$Description is missing: $Path"
    }

    $actualHash = Get-Sha256 -Path $Path
    if (-not [string]::Equals($actualHash, $ExpectedHash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "$Description hash mismatch. Expected ${ExpectedHash}, received ${actualHash}: $Path"
    }
}

foreach ($requiredPath in @($manifestPath, (Join-Path $pythonRoot "python.exe"), $venvPython)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Pinned Paddle runtime is incomplete: $requiredPath"
    }
}

$manifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
if ($manifest.runtimeId -ne $lock.runtimeId -or $manifest.platform -ne $lock.platform) {
    throw "Runtime manifest does not match the checked-in lock. Re-run tools/bootstrap-paddle-runtime.ps1."
}

$pythonVersion = (& $venvPython -I -B -c "import sys; print(sys.version.split()[0])").Trim()
if ($pythonVersion -ne $lock.python.version) {
    throw "Runtime CPython version mismatch. Expected $($lock.python.version), received $pythonVersion."
}

$probe = @'
import json
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
$probePath = Join-Path $runtimePath ".paddle-runtime-verify.py"
Set-Content -LiteralPath $probePath -Value $probe -Encoding utf8 -NoNewline
try {
    $probeOutput = & $venvPython -I -B $probePath
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned Paddle runtime verification probe failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -LiteralPath $probePath -Force -ErrorAction SilentlyContinue
}

$probeLine = $probeOutput | Where-Object { $_ -match '^\{.*\}$' } | Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($probeLine)) {
    throw "Pinned Paddle runtime verification probe did not return JSON."
}
$probeResult = $probeLine | ConvertFrom-Json
if ($probeResult.paddle -ne $lock.pythonPackages.paddlepaddleGpu -or $probeResult.paddleocr -ne $lock.pythonPackages.paddleOcr -or $probeResult.paddlex -ne $lock.pythonPackages.paddleX) {
    throw "Pinned Paddle package versions do not match the checked-in lock."
}
if (-not $SkipGpuProbe -and (-not [bool]$probeResult.cudaAvailable -or -not ([string]$probeResult.device).StartsWith("gpu", [System.StringComparison]::OrdinalIgnoreCase))) {
    throw "Pinned Paddle runtime has no CUDA GPU. Device='$($probeResult.device)'; CUDA available='$($probeResult.cudaAvailable)'."
}

foreach ($modelFile in $lock.model.files) {
    Assert-Sha256 -Path (Join-Path $modelRoot $modelFile.path) -ExpectedHash $modelFile.sha256 -Description "Paddle model '$($modelFile.path)'"
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
$modelProbePath = Join-Path $runtimePath ".paddle-model-verify.py"
Set-Content -LiteralPath $modelProbePath -Value $modelProbe -Encoding utf8 -NoNewline
try {
    $modelProbeOutput = & $venvPython -I -B $modelProbePath $modelRoot
    if ($LASTEXITCODE -ne 0) {
        throw "Pinned PP-OCRv6 model verification probe failed with exit code $LASTEXITCODE."
    }
}
finally {
    Remove-Item -LiteralPath $modelProbePath -Force -ErrorAction SilentlyContinue
}
$modelProbeLine = $modelProbeOutput | Where-Object { $_ -match '^\{.*\}$' } | Select-Object -Last 1
if ([string]::IsNullOrWhiteSpace($modelProbeLine)) {
    throw "Pinned PP-OCRv6 model verification probe did not return JSON."
}
$modelProbeResult = $modelProbeLine | ConvertFrom-Json
if ($modelProbeResult.inputCount -lt 1 -or $modelProbeResult.outputCount -lt 1) {
    throw "Pinned PP-OCRv6 model verification probe returned an invalid predictor interface."
}

foreach ($languagePack in $lock.tesseractLanguagePacks) {
    Assert-Sha256 -Path (Join-Path $tessdataRoot "$($languagePack.code).traineddata") -ExpectedHash $languagePack.sha256 -Description "Tesseract language pack '$($languagePack.code)'"
}

[ordered]@{
    status = "passed"
    runtimeId = $lock.runtimeId
    runtimeRoot = $runtimePath
    pythonVersion = $pythonVersion
    paddle = $probeResult.paddle
    paddleOcr = $probeResult.paddleocr
    paddleX = $probeResult.paddlex
    cudaAvailable = [bool]$probeResult.cudaAvailable
    device = $probeResult.device
    model = $lock.model.name
    modelProbe = [ordered]@{
        inputCount = $modelProbeResult.inputCount
        outputCount = $modelProbeResult.outputCount
        device = $modelProbeResult.device
    }
    tesseractLanguagePacks = @($lock.tesseractLanguagePacks.code)
} | ConvertTo-Json -Depth 4

$global:LASTEXITCODE = 0
