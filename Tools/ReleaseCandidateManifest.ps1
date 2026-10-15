# Shared read-only payload identity for actual Release acceptance and packaging.
# Notices are independently checked against their original sources during packaging.

function Get-PocketBumpReleaseManifestHash {
    param([Parameter(Mandatory=$true)][object[]]$Files)
    $byPath = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($entry in $Files) {
        $relative = [string]$entry.path
        $hash = [string]$entry.sha256
        if (!$relative -or $relative.Contains('\') -or $relative.Contains(':') -or
            $relative.StartsWith('/') -or $relative -match '(^|/)\.\.?(/|$)' -or
            $relative.Contains('//') -or $relative -match '[\x00-\x1f]' -or
            $hash -notmatch '^[0-9A-Fa-f]{64}$' -or $null -eq $entry.length) {
            throw 'Release manifest contains an invalid path, length or SHA256.'
        }
        $length = 0L
        if (![long]::TryParse([string]$entry.length, [ref]$length) -or $length -lt 0) {
            throw "Invalid release manifest file length: $relative"
        }
        if ($byPath.ContainsKey($relative)) { throw "Duplicate release manifest path: $relative" }
        $byPath.Add($relative, [pscustomobject]@{path=$relative; length=$length; sha256=$hash.ToUpperInvariant()})
    }
    [string[]]$paths = @($byPath.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $text = [Text.StringBuilder]::new()
    foreach ($path in $paths) {
        $entry = $byPath[$path]
        $null = $text.Append($entry.path).Append("`t").Append($entry.length.ToString([Globalization.CultureInfo]::InvariantCulture)).Append("`t").Append($entry.sha256).Append("`n")
    }
    $sha = [Security.Cryptography.SHA256]::Create()
    try { return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($text.ToString()))).Replace('-', '') }
    finally { $sha.Dispose() }
}

function Get-PocketBumpReleaseCandidateManifest {
    param([Parameter(Mandatory=$true)][string]$CandidateDirectory)
    if ($CandidateDirectory -notmatch '^[A-Za-z]:[\\/]') { throw 'CandidateDirectory must be a fully qualified local Windows path.' }
    $root = [IO.Path]::GetFullPath($CandidateDirectory).TrimEnd([char[]]'\/')
    if ($root -eq [IO.Path]::GetPathRoot($root).TrimEnd([char[]]'\/') -or
        !(Test-Path -LiteralPath $root -PathType Container)) { throw 'A concrete existing candidate directory is required, not a drive root.' }
    $cursor = $root
    while ($cursor) {
        $entry = Get-Item -LiteralPath $cursor -Force
        if (!$entry.PSIsContainer -or ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            throw "Release candidate ancestor is not a plain directory: $cursor"
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
    $root = (Get-Item -LiteralPath $root -Force).FullName.TrimEnd([char[]]'\/')
    $pending = [Collections.Generic.Stack[string]]::new(); $pending.Push($root)
    $byPath = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
    while ($pending.Count -gt 0) {
        $directory = $pending.Pop()
        # Inspect a single level first. Never descend into a junction/symlink.
        foreach ($entry in @(Get-ChildItem -LiteralPath $directory -Force)) {
            if ($entry.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Reparse point in release candidate: $($entry.FullName)"
            }
            if ($entry.PSIsContainer) {
                if ($entry.Name.EndsWith('_DoNotShip', [StringComparison]::OrdinalIgnoreCase) -or
                    ($directory -eq $root -and $entry.Name -ieq 'ThirdPartyNotices')) { continue }
                $pending.Push($entry.FullName); continue
            }
            if ($entry.Name -ieq 'WinPixEventRuntime.dll') { continue }
            $relative = $entry.FullName.Substring($root.Length + 1).Replace('\', '/')
            $length = $entry.Length
            $modified = $entry.LastWriteTimeUtc
            $hash = (Get-FileHash -LiteralPath $entry.FullName -Algorithm SHA256).Hash
            $entry.Refresh()
            if ($entry.Length -ne $length -or $entry.LastWriteTimeUtc -ne $modified) { throw "Candidate file changed while hashing: $relative" }
            $byPath.Add($relative, [pscustomobject]@{path=$relative; length=[long]$length; sha256=$hash})
        }
    }
    [string[]]$paths = @($byPath.Keys)
    [Array]::Sort($paths, [StringComparer]::Ordinal)
    $files = @($paths | ForEach-Object { $byPath[$_] })
    if ($files.Count -eq 0) { throw 'Release candidate contains no payload files.' }
    [pscustomobject]@{
        schema='pocketbump_release_candidate_manifest_v1'; files=$files;
        manifest_sha256=(Get-PocketBumpReleaseManifestHash -Files $files)
    }
}

function Assert-PocketBumpReleaseCandidateManifest {
    param(
        [Parameter(Mandatory=$true)][string]$CandidateDirectory,
        [Parameter(Mandatory=$true)][object[]]$ExpectedFiles,
        [Parameter(Mandatory=$true)][string]$ExpectedManifestSha256
    )
    if ($ExpectedFiles.Count -eq 0 -or $ExpectedManifestSha256 -notmatch '^[0-9A-Fa-f]{64}$' -or
        (Get-PocketBumpReleaseManifestHash -Files $ExpectedFiles) -ine $ExpectedManifestSha256) {
        throw 'Acceptance payload manifest is missing or its own checksum is invalid.'
    }
    $actual = Get-PocketBumpReleaseCandidateManifest -CandidateDirectory $CandidateDirectory
    if ($actual.files.Count -ne $ExpectedFiles.Count -or $actual.manifest_sha256 -ine $ExpectedManifestSha256) {
        throw 'Candidate payload differs from the accepted Release: file added, missing, renamed or changed.'
    }
    return $actual
}
