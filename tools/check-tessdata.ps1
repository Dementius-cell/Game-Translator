[CmdletBinding()]
param(
    [string]$TessdataPath,
    [switch]$Json,
    [switch]$FailOnMissing
)

$ErrorActionPreference = "Stop"

$requiredLanguages = @(
    [pscustomobject]@{ Code = "eng"; Name = "English" }
    [pscustomobject]@{ Code = "jpn"; Name = "Japanese" }
    [pscustomobject]@{ Code = "jpn_vert"; Name = "Japanese vertical" }
    [pscustomobject]@{ Code = "tha"; Name = "Thai" }
    [pscustomobject]@{ Code = "kor"; Name = "Korean" }
    [pscustomobject]@{ Code = "chi_sim"; Name = "Chinese simplified" }
    [pscustomobject]@{ Code = "chi_sim_vert"; Name = "Chinese simplified vertical" }
    [pscustomobject]@{ Code = "chi_tra"; Name = "Chinese traditional" }
    [pscustomobject]@{ Code = "chi_tra_vert"; Name = "Chinese traditional vertical" }
)

function Get-RepositoryRoot {
    $current = (Get-Item -LiteralPath $PSScriptRoot).FullName

    while (-not [string]::IsNullOrWhiteSpace($current)) {
        if (Test-Path -LiteralPath (Join-Path $current ".git")) {
            return $current
        }

        $parent = Split-Path -Path $current -Parent
        if ($parent -eq $current) {
            break
        }

        $current = $parent
    }

    return (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot "..")).Path
}

function Add-CandidatePath {
    param(
        [System.Collections.Generic.List[string]]$Paths,
        [string]$Path
    )

    if ([string]::IsNullOrWhiteSpace($Path)) {
        return
    }

    $trimmed = $Path.Trim().TrimEnd("\", "/")
    if (-not [string]::IsNullOrWhiteSpace($trimmed) -and -not $Paths.Contains($trimmed)) {
        $Paths.Add($trimmed)
    }
}

$repoRoot = Get-RepositoryRoot
$candidatePaths = [System.Collections.Generic.List[string]]::new()

Add-CandidatePath -Paths $candidatePaths -Path $TessdataPath
Add-CandidatePath -Paths $candidatePaths -Path $env:TESSDATA_PREFIX
Add-CandidatePath -Paths $candidatePaths -Path (Join-Path $repoRoot "tessdata")

$languageResults = foreach ($language in $requiredLanguages) {
    $fileName = "$($language.Code).traineddata"
    $foundPath = $null

    foreach ($candidatePath in $candidatePaths) {
        $candidateFile = Join-Path $candidatePath $fileName
        if (Test-Path -LiteralPath $candidateFile -PathType Leaf) {
            $foundPath = $candidateFile
            break
        }
    }

    [pscustomobject]@{
        code = $language.Code
        name = $language.Name
        fileName = $fileName
        found = $null -ne $foundPath
        path = $foundPath
    }
}

$missingLanguages = @($languageResults | Where-Object { -not $_.found })
$result = [pscustomobject]@{
    complete = $missingLanguages.Count -eq 0
    candidatePaths = @($candidatePaths)
    required = @($languageResults)
    missing = @($missingLanguages | ForEach-Object { $_.code })
}

if ($Json) {
    $result | ConvertTo-Json -Depth 4
}
else {
    Write-Host "Tesseract tessdata candidates:"
    foreach ($candidatePath in $candidatePaths) {
        $exists = Test-Path -LiteralPath $candidatePath -PathType Container
        $marker = if ($exists) { "present" } else { "missing" }
        Write-Host " - $candidatePath ($marker)"
    }

    Write-Host ""
    Write-Host "Required language data:"
    foreach ($languageResult in $languageResults) {
        $marker = if ($languageResult.found) { "OK" } else { "MISSING" }
        Write-Host (" - {0,-12} {1}" -f $languageResult.code, $marker)
    }
}

if ($FailOnMissing -and -not $result.complete) {
    exit 1
}
