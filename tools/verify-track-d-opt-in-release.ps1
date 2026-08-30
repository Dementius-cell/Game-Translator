[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Container })]
    [string]$ReleaseDirectory,

    [Parameter(Mandatory)]
    [ValidateScript({ Test-Path -LiteralPath $_ -PathType Leaf })]
    [string]$InputImagePath,

    [ValidateRange(1, 30)]
    [int]$StartupTimeoutSeconds = 5,

    [ValidateRange(1, 60)]
    [int]$ResultTimeoutSeconds = 15
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

trap {
    Write-Host "Candidate-detector package verification failed: $($_.Exception.Message)"
    exit 1
}

$releasePath = (Resolve-Path $ReleaseDirectory).Path
$python = Join-Path $releasePath "app\candidate-detector\python.exe"
$worker = Join-Path $releasePath "app\candidate-detector\paddle_text_detector_worker.py"
$reportPath = Join-Path $releasePath "candidate-detector-headless-verification.json"

foreach ($requiredPath in @($python, $worker)) {
    if (-not (Test-Path -LiteralPath $requiredPath)) {
        throw "Required packaged runtime file is missing: $requiredPath"
    }
}

$startInfo = [System.Diagnostics.ProcessStartInfo]::new()
$startInfo.FileName = $python
$startInfo.Arguments = ('"{0}" --worker' -f $worker.Replace('"', '\"'))
$startInfo.WorkingDirectory = Split-Path -Parent $worker
$startInfo.UseShellExecute = $false
$startInfo.CreateNoWindow = $true
$startInfo.RedirectStandardInput = $true
$startInfo.RedirectStandardOutput = $true
$startInfo.RedirectStandardError = $true
[void]$startInfo.EnvironmentVariables.Remove("PYTHONHOME")
[void]$startInfo.EnvironmentVariables.Remove("PYTHONPATH")
$startInfo.EnvironmentVariables["PYTHONNOUSERSITE"] = "1"
$startInfo.EnvironmentVariables["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True"

$process = [System.Diagnostics.Process]::new()
$process.StartInfo = $startInfo
$started = $false
$standardError = ""

$startedAt = [DateTimeOffset]::UtcNow
try {
    if (-not $process.Start()) {
        throw "Failed to start the packaged PaddleOCR worker."
    }
    $started = $true

    $readyTask = $process.StandardOutput.ReadLineAsync()
    if (-not $readyTask.Wait($StartupTimeoutSeconds * 1000)) {
        throw "The packaged worker did not become ready within $StartupTimeoutSeconds seconds. $standardError"
    }

    $readyLine = $readyTask.Result
    $ready = if ($null -eq $readyLine) { $null } else { $readyLine | ConvertFrom-Json }
    if ($null -eq $ready -or $ready.status -ne "ready") {
        throw "The packaged worker did not become ready within $StartupTimeoutSeconds seconds. $standardError"
    }

    $readyAt = [DateTimeOffset]::UtcNow
    $request = [ordered]@{
        inputPath = (Resolve-Path $InputImagePath).Path
        threshold = 0.3
        boxThreshold = 0.6
        unclipRatio = 1.2
    } | ConvertTo-Json -Compress
    $process.StandardInput.WriteLine($request)
    $process.StandardInput.Flush()

    $resultTask = $process.StandardOutput.ReadLineAsync()
    if (-not $resultTask.Wait($ResultTimeoutSeconds * 1000)) {
        throw "The packaged worker did not return a detection result within $ResultTimeoutSeconds seconds. $standardError"
    }

    $resultLine = $resultTask.Result
    $result = if ($null -eq $resultLine) { $null } else { $resultLine | ConvertFrom-Json }
    if ($null -eq $result -or $result.status -ne "ok") {
        $process.StandardInput.Close()
        [void]$process.WaitForExit(1000)
        $standardError = $process.StandardError.ReadToEnd().Trim()
        throw "The packaged worker did not return an ok detection result. Ready: '$readyLine'. Result: '$resultLine'. Standard error: $standardError"
    }

    $process.StandardInput.Close()
    [void]$process.WaitForExit(1000)
    $standardError = $process.StandardError.ReadToEnd().Trim()
    $completedAt = [DateTimeOffset]::UtcNow
    $report = [ordered]@{
        status = "passed"
        generatedAtUtc = $completedAt.ToString("O")
        packagedPython = $python
        worker = $worker
        inputImage = (Resolve-Path $InputImagePath).Path
        readyMs = [math]::Round(($readyAt - $startedAt).TotalMilliseconds, 1)
        firstResultMs = [math]::Round(($completedAt - $startedAt).TotalMilliseconds, 1)
        candidateCount = @($result.candidates).Count
        detectorThreshold = 0.3
        detectorBoxThreshold = 0.6
        detectorUnclipRatio = 1.2
        standardError = $standardError
    }
    $report | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $reportPath -Encoding utf8
    $report | ConvertTo-Json -Depth 4
}
finally {
    try {
        if ($started -and -not $process.HasExited) {
            $process.StandardInput.Close()
            if (-not $process.WaitForExit(1000)) {
                $process.Kill()
                $process.WaitForExit()
            }
        }
    }
    catch {
        # The worker can exit between HasExited and shutdown after sending its result.
    }
    finally {
        $process.Dispose()
    }
}
