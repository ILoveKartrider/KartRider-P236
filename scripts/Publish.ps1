[CmdletBinding()]
param(
    [ValidateSet('win-x64')]
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$artifactRoot = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts\publish'))
$expectedArtifactParent = [System.IO.Path]::GetFullPath((Join-Path $root 'artifacts'))
$expectedArtifactPrefix = $expectedArtifactParent.TrimEnd(
    [System.IO.Path]::DirectorySeparatorChar,
    [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $artifactRoot.StartsWith($expectedArtifactPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "Resolved artifact path escaped the repository: $artifactRoot"
}

function Assert-NoReparsePoint {
    param([Parameter(Mandatory)][string]$Path)

    $repositoryPrefix = $root.TrimEnd(
        [System.IO.Path]::DirectorySeparatorChar,
        [System.IO.Path]::AltDirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($repositoryPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escaped the repository: $fullPath"
    }

    $current = $root
    foreach ($segment in $fullPath.Substring($repositoryPrefix.Length) -split '[\\/]') {
        if ([string]::IsNullOrWhiteSpace($segment)) { continue }
        $current = Join-Path $current $segment
        if (-not (Test-Path -LiteralPath $current)) { break }
        $item = Get-Item -LiteralPath $current -Force
        if (($item.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
            throw "Refusing to use reparse-point artifact path: $current"
        }
    }

    if (-not (Test-Path -LiteralPath $fullPath -PathType Container)) { return }
    $pending = [System.Collections.Generic.Stack[System.IO.DirectoryInfo]]::new()
    $pending.Push([System.IO.DirectoryInfo](Get-Item -LiteralPath $fullPath -Force))
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        foreach ($entry in $directory.EnumerateFileSystemInfos()) {
            if (($entry.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw "Refusing to delete an artifact tree containing a reparse point: $($entry.FullName)"
            }
            if ($entry -is [System.IO.DirectoryInfo]) { $pending.Push($entry) }
        }
    }
}

function Invoke-DotNet {
    param([Parameter(Mandatory)][string[]]$Arguments)
    & dotnet @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet $($Arguments -join ' ') failed with exit code $LASTEXITCODE."
    }
}

function Assert-FrameworkDependentPublish {
    param(
        [Parameter(Mandatory)][string]$ConnectorOutput,
        [Parameter(Mandatory)][string]$ServerOutput
    )

    $runtimePayloadNames = @(
        'coreclr.dll', 'clrjit.dll', 'hostfxr.dll', 'hostpolicy.dll',
        'System.Private.CoreLib.dll', 'createdump.exe', 'mscordaccore.dll',
        'mscordbi.dll'
    )
    $runtimePayloads = @(
        Get-ChildItem -LiteralPath $ConnectorOutput, $ServerOutput -Recurse -File |
            Where-Object { $_.Name -in $runtimePayloadNames }
    )
    if ($runtimePayloads.Count -gt 0) {
        $paths = $runtimePayloads.FullName -join ', '
        throw "Framework-dependent publish unexpectedly contains .NET runtime payloads: $paths"
    }

    $maximumGuiExecutableBytes = 10MB
    foreach ($executable in @(
        (Join-Path $ConnectorOutput 'KartRider.P236.Connector.exe'),
        (Join-Path $ServerOutput 'KartRider.P236.Server.Launcher.exe')
    )) {
        if (-not (Test-Path -LiteralPath $executable -PathType Leaf)) {
            throw "Expected published executable was not found: $executable"
        }
        $length = (Get-Item -LiteralPath $executable).Length
        if ($length -gt $maximumGuiExecutableBytes) {
            throw "Published GUI executable exceeds the 10 MiB framework-dependent limit: " +
                "$executable ($length bytes)."
        }
    }

    foreach ($packageLimit in @(
        [pscustomobject]@{ Path = $ConnectorOutput; MaximumBytes = 10MB },
        [pscustomobject]@{ Path = $ServerOutput; MaximumBytes = 20MB }
    )) {
        $packageBytes = (
            Get-ChildItem -LiteralPath $packageLimit.Path -Recurse -File |
                Measure-Object -Property Length -Sum).Sum
        if ($packageBytes -gt $packageLimit.MaximumBytes) {
            throw "Published package exceeds its framework-dependent size limit: " +
                "$($packageLimit.Path) ($packageBytes bytes)."
        }
    }
}

function Get-DotNetNoticeFiles {
    $candidateRoots = [System.Collections.Generic.HashSet[string]]::new(
        [System.StringComparer]::OrdinalIgnoreCase)
    $dotnetCommand = Get-Command dotnet -ErrorAction Stop
    if (-not [string]::IsNullOrWhiteSpace($dotnetCommand.Source)) {
        [void]$candidateRoots.Add((Split-Path -Parent $dotnetCommand.Source))
    }
    foreach ($environmentRoot in @($env:DOTNET_ROOT, $env:DOTNET_ROOT_X64)) {
        if (-not [string]::IsNullOrWhiteSpace($environmentRoot)) {
            [void]$candidateRoots.Add([System.IO.Path]::GetFullPath($environmentRoot))
        }
    }

    $sdkLines = @(& dotnet --list-sdks)
    if ($LASTEXITCODE -ne 0) {
        throw "dotnet --list-sdks failed with exit code $LASTEXITCODE."
    }
    foreach ($line in $sdkLines) {
        if ($line -match '\[(?<sdkDirectory>.+)\]\s*$') {
            [void]$candidateRoots.Add((Split-Path -Parent $Matches.sdkDirectory))
        }
    }

    foreach ($candidateRoot in $candidateRoots) {
        $licensePath = Join-Path $candidateRoot 'LICENSE.txt'
        $noticePath = Join-Path $candidateRoot 'ThirdPartyNotices.txt'
        if ((Test-Path -LiteralPath $licensePath -PathType Leaf) -and
            (Test-Path -LiteralPath $noticePath -PathType Leaf)) {
            return [pscustomobject]@{
                License = $licensePath
                Notices = $noticePath
            }
        }
    }

    throw "The active .NET SDK license and third-party notice files were not found. " +
        "Refusing to create an incomplete redistribution package."
}

& (Join-Path $PSScriptRoot 'Test-SourceBoundary.ps1')
$dotNetNotices = Get-DotNetNoticeFiles

Assert-NoReparsePoint -Path $artifactRoot
if (Test-Path -LiteralPath $artifactRoot) {
    Remove-Item -LiteralPath $artifactRoot -Recurse -Force
}

$connectorOutput = Join-Path $artifactRoot 'connector'
$serverOutput = Join-Path $artifactRoot 'server'

Push-Location $root
try {
    Invoke-DotNet @(
        'publish', '.\src\KartRider.P236.Connector\KartRider.P236.Connector.csproj',
        '-c', 'Release', '-r', $Runtime, '--self-contained', 'false',
        '-o', $connectorOutput
    )
    Invoke-DotNet @(
        'publish', '.\src\KartRider.P236.Server.Host\KartRider.P236.Server.Host.csproj',
        '-c', 'Release', '-r', $Runtime, '--self-contained', 'false',
        '-o', $serverOutput
    )
    Invoke-DotNet @(
        'publish', '.\src\KartRider.P236.Server.Launcher\KartRider.P236.Server.Launcher.csproj',
        '-c', 'Release', '-r', $Runtime, '--self-contained', 'false',
        '-o', $serverOutput
    )

    Assert-FrameworkDependentPublish -ConnectorOutput $connectorOutput -ServerOutput $serverOutput

    foreach ($output in @($connectorOutput, $serverOutput)) {
        Copy-Item -LiteralPath '.\LICENSE.md' -Destination $output
        Copy-Item -LiteralPath '.\NOTICE.md' -Destination $output
        Copy-Item -LiteralPath '.\THIRD_PARTY_NOTICES.md' -Destination $output
        Copy-Item -LiteralPath '.\LEGAL.md' -Destination $output
        Copy-Item -LiteralPath $dotNetNotices.License -Destination (Join-Path $output 'DOTNET-LICENSE.txt')
        Copy-Item -LiteralPath $dotNetNotices.Notices -Destination (Join-Path $output 'DOTNET-THIRD-PARTY-NOTICES.txt')
    }
    Copy-Item -LiteralPath '.\src\KartRider.P236.ItemProbabilities\NOTICE.md' `
        -Destination (Join-Path $serverOutput 'ITEM-PROBABILITY-NOTICE.md')
}
finally {
    Pop-Location
}

Write-Host "Published connector: $connectorOutput"
Write-Host "Published server:    $serverOutput"
Write-Host "Packaging mode:      framework-dependent (.NET 8 runtime not included)"
