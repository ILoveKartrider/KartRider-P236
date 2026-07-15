[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$rootPrefix = $root.TrimEnd([System.IO.Path]::DirectorySeparatorChar) +
    [System.IO.Path]::DirectorySeparatorChar
function Get-RepositoryRelativePath {
    param([Parameter(Mandatory)][string]$Path)

    $fullPath = [System.IO.Path]::GetFullPath($Path)
    if (-not $fullPath.StartsWith($rootPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path escaped the repository: $fullPath"
    }

    return $fullPath.Substring($rootPrefix.Length)
}

$excludedSegments = @('.git', 'bin', 'obj', 'artifacts', 'publish', 'TestResults', '.vs', '.idea')
$forbiddenSegmentPatterns = @(
    '^(bin|obj|artifacts|publish|TestResults)$',
    '^(analysis|captures|decompiled|ida|x64dbg|clients)(?:[-_].*)?$',
    '^stripper',
    '^HF_(?:.+)$',
    '^reverse'
)
$forbiddenNames = @(
    'KartRider.exe', 'KartRider.pin', 'KartRider.xml', 'launcher.xml',
    'profiles.json', 'observers.json', 'item-probabilities.json',
    'server-launcher.json', 'p236-packets.log', '.gitmodules'
)
$forbiddenExtensions = @(
    '.exe', '.dll', '.pdb', '.rho', '.rho5', '.bml', '.ksv', '.1s', '.sg',
    '.dds', '.tga', '.pcap', '.pcapng', '.dmp', '.idb', '.id0', '.id1',
    '.i64', '.nam', '.til', '.rar', '.7z', '.zip', '.iso', '.vhd', '.vhdx',
    '.avhdx', '.db', '.sqlite', '.sqlite3', '.log', '.bak', '.tmp', '.bin',
    '.dat', '.pfx', '.p12', '.pem', '.key', '.snk', '.evtx', '.png', '.jpg',
    '.jpeg', '.gif', '.webp', '.ico', '.wav', '.mp3', '.ogg', '.mp4',
    '.htm', '.html', '.mht', '.mhtml', '.pdf', '.doc', '.docx', '.chm',
    '.rtf', '.odt', '.asm', '.lst', '.map', '.mdmp', '.etl', '.apk', '.msi',
    '.msix', '.tar', '.gz', '.tgz', '.bz2', '.xz', '.ova', '.ovf', '.vdi',
    '.vmdk', '.qcow2', '.wasm'
)
$sensitivePatterns = [ordered]@{
    'absolute Windows path' = '(?<![A-Za-z0-9])[A-Za-z]:\\[^\s"'']+'
    'absolute home path'    = '(?<![A-Za-z0-9])/(?:home|Users)/[^/\s"'']+'
    'GitHub token'          = '(?:ghp_|github_pat_)[A-Za-z0-9_]+'
    'OpenAI token'          = 'sk-[A-Za-z0-9_-]{20,}'
    'AWS access key'        = '(?<![A-Z0-9])(?:AKIA|ASIA)[A-Z0-9]{16}(?![A-Z0-9])'
    'GitLab token'          = 'glpat-[A-Za-z0-9_-]{20,}'
    'Slack token'           = 'xox[baprs]-[A-Za-z0-9-]{10,}'
    'private key'           = '-----BEGIN [A-Z ]*PRIVATE KEY-----'
}
$violations = [System.Collections.Generic.List[string]]::new()
$gitDirectory = Join-Path $root '.git'
if (Test-Path -LiteralPath $gitDirectory) {
    $relativeFiles = @(& git -C $root ls-files --cached --others --exclude-standard)
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files failed with exit code $LASTEXITCODE."
    }

    $specialEntries = @(& git -C $root ls-files --stage | Where-Object {
        $_ -match '^(120000|160000)\s'
    })
    if ($LASTEXITCODE -ne 0) {
        throw "git ls-files --stage failed with exit code $LASTEXITCODE."
    }
    foreach ($entry in $specialEntries) {
        $violations.Add("tracked symlink or submodule is not allowed: $entry")
    }

    $files = @($relativeFiles | Where-Object { $_ } | ForEach-Object {
        $candidate = Join-Path $root $_
        if (Test-Path -LiteralPath $candidate) {
            Get-Item -LiteralPath $candidate -Force
        }
    })
}
else {
    # Bootstrap mode before `git init`: omit only generated output. Once the
    # repository exists, git ls-files above also exposes force-tracked files in
    # ignored directories, so CI cannot hide a binary under bin/obj/artifacts.
    $files = @(Get-ChildItem -LiteralPath $root -Recurse -Force | Where-Object {
        $relative = Get-RepositoryRelativePath $_.FullName
        $segments = $relative -split '[\\/]'
        $isReparsePoint = ($_.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0
        (-not $_.PSIsContainer -or $isReparsePoint) -and
            -not ($segments | Where-Object { $_ -in $excludedSegments })
    })
}

foreach ($file in $files) {
    $relative = Get-RepositoryRelativePath $file.FullName
    $segments = $relative -split '[\\/]'

    if (($file.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) {
        $violations.Add("filesystem reparse point is not allowed: $relative")
        continue
    }
    if ($file.PSIsContainer) {
        $violations.Add("tracked directory or submodule is not allowed: $relative")
        continue
    }

    foreach ($segment in $segments) {
        foreach ($pattern in $forbiddenSegmentPatterns) {
            if ($segment -match $pattern) {
                $violations.Add("forbidden path segment '$segment': $relative")
                break
            }
        }
    }

    if ($file.Name -in $forbiddenNames) {
        $violations.Add("proprietary client filename: $relative")
    }

    if ($file.Extension.ToLowerInvariant() -in $forbiddenExtensions) {
        $violations.Add("forbidden binary/data extension '$($file.Extension)': $relative")
    }

    if ($file.Length -gt 2MB) {
        $violations.Add("unexpected file larger than 2 MiB: $relative")
    }

    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    if ($bytes -contains 0) {
        $violations.Add("binary NUL byte detected: $relative")
        continue
    }

    $text = [System.Text.Encoding]::UTF8.GetString($bytes)

    # The GUI permits arbitrary JSON filenames. Detect exported probability
    # tables by their schema so renaming item-probabilities.json cannot bypass
    # the public-source boundary.
    if ($file.Extension.Equals('.json', [System.StringComparison]::OrdinalIgnoreCase)) {
        try {
            $json = $text | ConvertFrom-Json -ErrorAction Stop
            $propertyNames = @($json.PSObject.Properties.Name)
            $probabilityProperties = @(
                'version', 'individual', 'team', 'flag',
                'individualBonus', 'teamBonus'
            )
            if (@($probabilityProperties | Where-Object { $_ -notin $propertyNames }).Count -eq 0) {
                $violations.Add("generated item-probability JSON is not allowed: $relative")
            }
        }
        catch {
            # Malformed JSON is handled by normal review/build tooling; this
            # check only identifies the exported probability schema.
        }
    }

    foreach ($entry in $sensitivePatterns.GetEnumerator()) {
        if ($text -match $entry.Value) {
            $violations.Add("$($entry.Key): $relative")
        }
    }

}

if ($violations.Count -gt 0) {
    Write-Error ("Source-boundary check failed:`n - " + (($violations | Sort-Object -Unique) -join "`n - "))
}

Write-Host "Source-boundary check passed: $($files.Count) files inspected."
