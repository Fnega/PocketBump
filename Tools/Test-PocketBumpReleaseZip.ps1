[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) {
    throw 'Run this fixture with Windows PowerShell 5.1 (powershell.exe).'
}
. (Join-Path $PSScriptRoot 'ReleaseZipArchive.ps1')

$fixtureParent = Join-Path $PSScriptRoot 'ReleaseResults'
$null = [IO.Directory]::CreateDirectory($fixtureParent)
$fixtureRoot = Join-Path $fixtureParent ('ReleaseZipFixture-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($fixtureRoot)
$checks = 0
$skipped = [Collections.Generic.List[string]]::new()

function Assert-ZipTrue([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw "Release ZIP fixture failed: $Label" }
    $script:checks++
}
function Assert-ZipRejected([scriptblock]$Action, [string]$Label) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert-ZipTrue $rejected $Label
}
function New-ZipFixtureDirectory([string]$Relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $fixtureRoot $Relative))
    if (!$path.StartsWith($fixtureRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture path escaped its owned directory.' }
    $null = [IO.Directory]::CreateDirectory($path)
    return $path
}
function Write-ZipFixtureFile([string]$Directory, [string]$Relative, [byte[]]$Bytes) {
    $path = [IO.Path]::GetFullPath((Join-Path $Directory $Relative))
    if (!$path.StartsWith($fixtureRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture file escaped its owned directory.' }
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllBytes($path, $Bytes)
    return $path
}
function Get-ZipFixtureSnapshot([string]$Directory) {
    $rows = @(Get-ChildItem -LiteralPath $Directory -Recurse -File -Force | Sort-Object FullName | ForEach-Object {
        $_.FullName.Substring($Directory.Length + 1) + '|' + $_.Length + '|' + $_.LastWriteTimeUtc.Ticks + '|' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash
    })
    return [string]::Join("`n", $rows)
}

try {
    $source = New-ZipFixtureDirectory 'PocketBump1.0'
    $chinese = -join @([char]0x5C04, [char]0x7403, [char]0x5927, [char]0x6218)
    $expected = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    $expected.Add('empty.bin', (Write-ZipFixtureFile $source 'empty.bin' ([byte[]]@())))
    $expected.Add('read me.txt', (Write-ZipFixtureFile $source 'read me.txt' ([Text.Encoding]::UTF8.GetBytes('ordinary text with spaces'))))
    $expected.Add('PocketBump_Data/Managed/test runtime.dll', (Write-ZipFixtureFile $source 'PocketBump_Data/Managed/test runtime.dll' ([byte[]]@(0, 1, 2, 10, 13, 127, 128, 255))))
    $unicodeRelative = $chinese + ' folder/' + $chinese + '.txt'
    $expected.Add($unicodeRelative, (Write-ZipFixtureFile $source $unicodeRelative ([Text.Encoding]::UTF8.GetBytes($chinese))))
    $expected.Add('nested/deeper/zero-byte.txt', (Write-ZipFixtureFile $source 'nested/deeper/zero-byte.txt' ([byte[]]@())))
    $hidden = Write-ZipFixtureFile $source 'hidden-file.txt' ([Text.Encoding]::UTF8.GetBytes('hidden but required'))
    [IO.File]::SetAttributes($hidden, [IO.FileAttributes]::Hidden)
    $expected.Add('hidden-file.txt', $hidden)
    $null = New-ZipFixtureDirectory 'PocketBump1.0/empty-directory'
    $before = Get-ZipFixtureSnapshot $source
    $zipPath = Join-Path $fixtureRoot 'package with spaces.zip'
    New-PocketBumpReleaseZip -SourceDirectory $source -ZipPath $zipPath
    Assert-ZipTrue (Test-Path -LiteralPath $zipPath -PathType Leaf) 'new archive exists'
    Assert-ZipTrue ((Get-ZipFixtureSnapshot $source) -ceq $before) 'archive creation does not change source bytes or timestamps'
    $archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
    try {
        Assert-ZipTrue ($archive.Entries.Count -eq $expected.Count) 'only regular source files appear; no extra root or directory entries'
        $seen = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
        foreach ($entry in $archive.Entries) {
            Assert-ZipTrue ($seen.Add($entry.FullName)) 'ZIP contains no duplicate names'
            Assert-ZipTrue (!$entry.FullName.Contains('\')) 'entry uses forward slash separators only'
            Assert-ZipTrue ($entry.FullName.StartsWith('PocketBump1.0/', [StringComparison]::Ordinal)) 'every entry has exactly the expected single source-folder root'
            $relative = $entry.FullName.Substring('PocketBump1.0/'.Length)
            Assert-ZipTrue ($expected.ContainsKey($relative)) 'entry is one expected relative file and no extras'
            Assert-ZipTrue ($entry.Length -eq (Get-Item -LiteralPath $expected[$relative] -Force).Length) 'file length including zero bytes is retained'
            $stream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $sha.Dispose(); $stream.Dispose() }
            Assert-ZipTrue ($hash -ceq (Get-FileHash -LiteralPath $expected[$relative] -Algorithm SHA256).Hash) 'entry content SHA256 matches source'
        }
        foreach ($relative in $expected.Keys) { Assert-ZipTrue ($null -ne $archive.GetEntry('PocketBump1.0/' + $relative)) 'forward-slash lookup finds every expected file' }
    } finally { $archive.Dispose() }

    $unicodeSource = New-ZipFixtureDirectory ($chinese + ' source with spaces')
    $null = Write-ZipFixtureFile $unicodeSource 'a.txt' ([Text.Encoding]::UTF8.GetBytes('root test'))
    $unicodeZip = Join-Path $fixtureRoot ($chinese + ' archive.zip')
    New-PocketBumpReleaseZip -SourceDirectory $unicodeSource -ZipPath $unicodeZip
    $archive = [IO.Compression.ZipFile]::OpenRead($unicodeZip)
    try {
        Assert-ZipTrue ($archive.Entries.Count -eq 1) 'Unicode source folder produces no additional entries'
        Assert-ZipTrue ($archive.Entries[0].FullName -ceq ($chinese + ' source with spaces/a.txt')) 'source folder name is retained exactly, including Chinese and spaces'
    } finally { $archive.Dispose() }

    $existingHash = (Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $source -ZipPath $zipPath } 'existing valid archive cannot be overwritten'
    Assert-ZipTrue ((Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash -ceq $existingHash) 'existing valid archive remains unchanged'
    $sentinel = Write-ZipFixtureFile $fixtureRoot 'existing-not-a-zip.zip' ([Text.Encoding]::UTF8.GetBytes('preserve this preexisting non-zip file'))
    $sentinelHash = (Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $source -ZipPath $sentinel } 'existing arbitrary output file cannot be overwritten'
    Assert-ZipTrue ((Get-FileHash -LiteralPath $sentinel -Algorithm SHA256).Hash -ceq $sentinelHash) 'preexisting non-zip bytes remain unchanged'
    $inside = Join-Path $source 'nested/inside.zip'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $source -ZipPath $inside } 'archive inside source is rejected'
    Assert-ZipTrue (!(Test-Path -LiteralPath $inside)) 'inside-source archive was not created'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $source -ZipPath $source } 'source itself cannot be used as destination'
    $missingParent = Join-Path $fixtureRoot 'must-not-be-created'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $source -ZipPath (Join-Path $missingParent 'a.zip') } 'missing destination parent is rejected'
    Assert-ZipTrue (!(Test-Path -LiteralPath $missingParent)) 'helper does not create a missing output parent'
    $rejectedZip = Join-Path $fixtureRoot 'rejected.zip'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory (Join-Path $fixtureRoot 'missing-source') -ZipPath $rejectedZip } 'missing source is rejected'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $sentinel -ZipPath $rejectedZip } 'source file instead of directory is rejected'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory '.\relative-source' -ZipPath $rejectedZip } 'relative source is rejected'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory 'D:drive-relative-source' -ZipPath $rejectedZip } 'drive-relative source is rejected'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $source -ZipPath '.\relative.zip' } 'relative archive path is rejected'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory ([IO.Path]::GetPathRoot($fixtureRoot)) -ZipPath $rejectedZip } 'drive root source is rejected before enumeration'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $source -ZipPath $unicodeSource } 'preexisting directory is not replaced by a ZIP'
    Assert-ZipTrue (!(Test-Path -LiteralPath $rejectedZip)) 'invalid inputs leave no archive behind'
    Assert-ZipTrue ((Get-ZipFixtureSnapshot $source) -ceq $before) 'all invalid-path requests leave the original source unchanged'

    $outside = New-ZipFixtureDirectory 'owned-link-target'
    $outsideFile = Write-ZipFixtureFile $outside 'keep.txt' ([Text.Encoding]::UTF8.GetBytes('owned target must not be followed or changed'))
    $outsideBefore = Get-ZipFixtureSnapshot $outside
    $nestedSource = New-ZipFixtureDirectory 'nested-junction-source'
    $nestedJunction = Join-Path $nestedSource 'linked-directory'
    $null = New-Item -ItemType Junction -Path $nestedJunction -Target $outside
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $nestedSource -ZipPath $rejectedZip } 'nested reparse directory is rejected before following it'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $nestedJunction -ZipPath $rejectedZip } 'source itself cannot be a reparse directory'
    $ancestorReal = New-ZipFixtureDirectory 'ancestor-real/child'
    $null = Write-ZipFixtureFile $ancestorReal 'keep.txt' ([Text.Encoding]::UTF8.GetBytes('ancestor fixture'))
    $ancestorLink = Join-Path $fixtureRoot 'ancestor-link'
    $null = New-Item -ItemType Junction -Path $ancestorLink -Target ([IO.Path]::GetDirectoryName($ancestorReal))
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory (Join-Path $ancestorLink 'child') -ZipPath $rejectedZip } 'reparse ancestor of source is rejected'
    Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $source -ZipPath (Join-Path $ancestorLink 'child/rejected.zip') } 'reparse ancestor of output is rejected'
    Assert-ZipTrue (!(Test-Path -LiteralPath (Join-Path $ancestorReal 'rejected.zip'))) 'output ancestor link target was not written'
    Assert-ZipTrue (!(Test-Path -LiteralPath $rejectedZip)) 'reparse directory rejections create no output'
    Assert-ZipTrue ((Get-ZipFixtureSnapshot $outside) -ceq $outsideBefore) 'nested link target remains unchanged'

    $fileLinkSource = New-ZipFixtureDirectory 'file-symlink-source'
    $fileLink = Join-Path $fileLinkSource 'linked-file.txt'
    $fileLinkCreated = $false
    try { $null = New-Item -ItemType SymbolicLink -Path $fileLink -Target $outsideFile; $fileLinkCreated = $true }
    catch {
        # Windows PowerShell 5.1 requires admin for New-Item SymbolicLink even on
        # systems supporting unprivileged developer-mode links. Ask the native
        # API without elevation; Windows still enforces its existing policy.
        $fileLinkFailure = $_.Exception.Message
        Add-Type -TypeDefinition @'
using System.Runtime.InteropServices;
namespace PocketBump.ReleaseZipFixture {
    public static class NativeLinks {
        [DllImport("kernel32.dll", EntryPoint = "CreateSymbolicLinkW", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.I1)]
        public static extern bool CreateFileLink(string link, string target, int flags);
    }
}
'@
        $fileLinkCreated = [PocketBump.ReleaseZipFixture.NativeLinks]::CreateFileLink($fileLink, $outsideFile, 2)
        if (!$fileLinkCreated) {
            $linkError = [Runtime.InteropServices.Marshal]::GetLastWin32Error()
            $skipped.Add('File-symbolic-link fixture unavailable under existing OS policy: ' + $fileLinkFailure + '; native error ' + $linkError)
        }
    }
    if ($fileLinkCreated) {
        Assert-ZipTrue ([bool]((Get-Item -LiteralPath $fileLink -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) 'file link is a real reparse point'
        Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $fileLinkSource -ZipPath $rejectedZip } 'reparse file in source is rejected'
        Assert-ZipRejected { New-PocketBumpReleaseZip -SourceDirectory $source -ZipPath $fileLink } 'reparse output file is rejected'
        Assert-ZipTrue (!(Test-Path -LiteralPath $rejectedZip)) 'file reparse rejection creates no archive'
        Assert-ZipTrue ((Get-ZipFixtureSnapshot $outside) -ceq $outsideBefore) 'file link target remains unchanged'
    }
    Assert-ZipTrue ((Get-ZipFixtureSnapshot $source) -ceq $before) 'all tests preserve the original source tree'
    [pscustomobject]@{
        status = $(if ($skipped.Count -eq 0) { 'passed' } else { 'passed_with_skipped_fixture' })
        checks = $checks
        skipped = @($skipped)
        powershell_version = $PSVersionTable.PSVersion.ToString()
        fixture = $fixtureRoot
        cleanup = 'None: this named project-local fixture is retained for audit; no real release was modified.'
    } | ConvertTo-Json -Depth 4
} catch {
    Write-Warning "Failed fixture retained for audit: $fixtureRoot"
    throw
}
