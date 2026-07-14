[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$solution = Join-Path $root 'KartRider.P236.sln'

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

& (Join-Path $PSScriptRoot 'Test-SourceBoundary.ps1')
if (-not (Test-Path -LiteralPath $solution)) {
    throw "Solution not found: $solution"
}

Push-Location $root
try {
    Invoke-DotNet @('restore', $solution)
    Invoke-DotNet @('build', $solution, '-c', $Configuration, '--no-restore')
    Invoke-DotNet @('test', $solution, '-c', $Configuration, '--no-build')
}
finally {
    Pop-Location
}
