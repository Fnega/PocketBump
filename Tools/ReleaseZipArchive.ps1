# Explicit ZIP entry names avoid .NET Framework's platform-dependent separators.
function New-PocketBumpReleaseZip {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory=$true)][string]$SourceDirectory,
        [Parameter(Mandatory=$true)][string]$ZipPath
    )

    foreach ($candidatePath in @($SourceDirectory, $ZipPath)) {
        if ($candidatePath -notmatch '^[A-Za-z]:[\\/]') {
            throw 'Release ZIP paths must be fully qualified local Windows paths.'
        }
        $cursor = [IO.Path]::GetFullPath($candidatePath)
        while ($cursor) {
            if ((Test-Path -LiteralPath $cursor) -and
                ((Get-Item -LiteralPath $cursor -Force -ErrorAction Stop).Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Reparse points are not accepted in release ZIP paths: $cursor"
            }
            $cursor = [IO.Path]::GetDirectoryName($cursor)
        }
    }
    if (!(Test-Path -LiteralPath $SourceDirectory -PathType Container)) {
        throw 'Release ZIP source directory must exist.'
    }
    $source = (Resolve-Path -LiteralPath $SourceDirectory -ErrorAction Stop).ProviderPath.TrimEnd([char[]]'\/')
    $zip = [IO.Path]::GetFullPath($ZipPath)
    $rootName = [IO.Path]::GetFileName($source)
    if (!$rootName -or $source -eq [IO.Path]::GetPathRoot($source).TrimEnd([char[]]'\/')) {
        throw 'A drive root cannot be a release ZIP source.'
    }
    if ($zip.Equals($source, [StringComparison]::OrdinalIgnoreCase) -or
        $zip.StartsWith($source + '\', [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Release ZIP must be outside the source directory.'
    }
    if (Test-Path -LiteralPath $zip) { throw 'Release ZIP already exists; nothing was overwritten.' }
    if (!(Test-Path -LiteralPath ([IO.Path]::GetDirectoryName($zip)) -PathType Container)) {
        throw 'Release ZIP parent directory must exist.'
    }

    # Inspect one level at a time so a nested junction is rejected before descent.
    $pending = [Collections.Generic.Stack[string]]::new()
    $files = [Collections.Generic.List[IO.FileInfo]]::new()
    $pending.Push($source)
    while ($pending.Count -gt 0) {
        foreach ($item in @(Get-ChildItem -LiteralPath $pending.Pop() -Force -ErrorAction Stop)) {
            if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Reparse points are not accepted in release ZIP contents: $($item.FullName)"
            }
            if ($item.PSIsContainer) { $pending.Push($item.FullName) }
            else { $files.Add($item) }
        }
    }

    Add-Type -AssemblyName System.IO.Compression -ErrorAction Stop
    Add-Type -AssemblyName System.IO.Compression.FileSystem -ErrorAction Stop
    $zipStream = [IO.File]::Open($zip, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $archive = $null
    try {
        $archive = [IO.Compression.ZipArchive]::new($zipStream, [IO.Compression.ZipArchiveMode]::Create, $true)
        foreach ($file in @($files | Sort-Object FullName)) {
            $relative = $file.FullName.Substring($source.Length + 1).Replace('\', '/')
            $entry = $archive.CreateEntry($rootName + '/' + $relative, [IO.Compression.CompressionLevel]::Optimal)
            $sourceStream = $null
            $entryStream = $null
            try {
                $sourceStream = [IO.File]::OpenRead($file.FullName)
                $entryStream = $entry.Open()
                $sourceStream.CopyTo($entryStream)
            } finally {
                try { if ($null -ne $entryStream) { $entryStream.Dispose() } }
                finally { if ($null -ne $sourceStream) { $sourceStream.Dispose() } }
            }
        }
    } finally {
        try { if ($null -ne $archive) { $archive.Dispose() } }
        finally { $zipStream.Dispose() }
    }
}
