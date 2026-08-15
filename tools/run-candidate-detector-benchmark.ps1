[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$InputPath,

    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$OutputPath,

    [switch]$Force
)

$projectPath = Join-Path $PSScriptRoot 'ComicGeometryCandidateDetectorBenchmark/ComicGeometryCandidateDetectorBenchmark.csproj'
$runnerArguments = @(
    'run',
    '--project', $projectPath,
    '--configuration', 'Release',
    '--',
    '--input', $InputPath,
    '--output', $OutputPath
)

if ($Force) {
    $runnerArguments += '--force'
}

& dotnet @runnerArguments
exit $LASTEXITCODE
