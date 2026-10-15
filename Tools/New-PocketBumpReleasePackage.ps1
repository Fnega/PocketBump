[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$CandidateDirectory,
    [Parameter(Mandatory=$true)][string]$DestinationDirectory,
    [Parameter(Mandatory=$true)][string]$AcceptancePath,
    [ValidateSet('1.0', '1.1', '1.2')][string]$Version = '1.2'
)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseCandidateManifest.ps1')
. (Join-Path $PSScriptRoot 'ReleaseZipArchive.ps1')

function Assert-PlainPath([string]$Path) {
    $cursor = $Path
    while ($cursor) {
        if (Test-Path -LiteralPath $cursor) {
            if ((Get-Item -LiteralPath $cursor -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
                throw "Reparse points are not accepted in release paths: $cursor"
            }
        }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
foreach ($path in @($CandidateDirectory, $DestinationDirectory)) {
    if ($path -notmatch '^[A-Za-z]:[\\/]') { throw 'Use fully qualified local Windows paths.' }
    Assert-PlainPath ([IO.Path]::GetFullPath($path))
}
$candidate = (Resolve-Path -LiteralPath $CandidateDirectory).ProviderPath.TrimEnd([char[]]'\/')
$destination = [IO.Path]::GetFullPath($DestinationDirectory).TrimEnd([char[]]'\/')
$packageName = 'PocketBump' + $Version
if ([IO.Path]::GetFileName($destination) -cne $packageName) { throw "The $Version delivery folder must be named $packageName." }
if ($candidate -eq $destination -or $destination.StartsWith($candidate + '\', [StringComparison]::OrdinalIgnoreCase) -or
    $candidate.StartsWith($destination + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Candidate and delivery paths must be independent.' }
$zipPath = $destination + '.zip'
if ((Test-Path -LiteralPath $destination) -or (Test-Path -LiteralPath $zipPath)) { throw 'Release folder or zip already exists; nothing was overwritten.' }
$parent = [IO.Path]::GetDirectoryName($destination)
if (!(Test-Path -LiteralPath $parent -PathType Container)) { throw 'The explicitly selected destination parent must already exist.' }
foreach ($relative in @('PocketBump.exe', 'UnityPlayer.dll', 'PocketBump_Data/Managed/PocketBump.Runtime.dll', 'build-info.json')) {
    if (!(Test-Path -LiteralPath (Join-Path $candidate $relative) -PathType Leaf)) { throw "Missing candidate file: $relative" }
}
$identity = Get-Content -LiteralPath (Join-Path $candidate 'build-info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
$expectedProtocol = if ($Version -ceq '1.0') { 7 } else { 8 }
if ($identity.development -ne $false -or $identity.version -cne $Version -or $identity.developer -cne 'Fnega' -or
    $identity.protocol -ne $expectedProtocol -or !$identity.build_guid) {
    throw "Candidate must identify a non-Development $Version Fnega build using protocol $expectedProtocol."
}
$peVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo((Join-Path $candidate 'PocketBump.exe'))
if ($peVersion.CompanyName -cne 'Fnega' -or $peVersion.ProductName -cne 'PocketBump' -or
    $peVersion.FileDescription -cne '射球大战 / PocketBump' -or $peVersion.ProductVersion -cne $Version -or
    $peVersion.FileVersion -cne ($Version + '.0.0')) {
    throw "Candidate executable PE version metadata must identify Fnega / PocketBump / 射球大战 / PocketBump / $Version / $Version.0.0."
}
$acceptance = Get-Content -LiteralPath $AcceptancePath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($acceptance.schema -cne 'pocketbump_release_ui_acceptance_v1' -or
    $acceptance.candidate_manifest_schema -cne 'pocketbump_release_candidate_manifest_v1' -or
    $acceptance.passed -ne $true -or $acceptance.expected_version -cne $Version -or
    $acceptance.expected_build_guid.Replace('-', '') -ine $identity.build_guid.Replace('-', '') -or
    $acceptance.player_sha256 -cne (Get-FileHash -LiteralPath (Join-Path $candidate 'PocketBump.exe') -Algorithm SHA256).Hash -or
    $acceptance.runtime_assembly_sha256 -cne (Get-FileHash -LiteralPath (Join-Path $candidate 'PocketBump_Data/Managed/PocketBump.Runtime.dll') -Algorithm SHA256).Hash -or
    $acceptance.build_info_sha256 -cne (Get-FileHash -LiteralPath (Join-Path $candidate 'build-info.json') -Algorithm SHA256).Hash) {
    throw 'A passed actual-Release acceptance record matching these exact candidate bits is required.'
}
$candidateManifest = Assert-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidate `
    -ExpectedFiles @($acceptance.candidate_files) -ExpectedManifestSha256 $acceptance.candidate_manifest_sha256
$documents = @('试玩说明.txt', '双电脑验收.txt', 'release-verification.txt')
$documentDirectory = Join-Path $PSScriptRoot ('Release/' + $Version)
# Preserve the reviewed 1.0 documents at their original location. A new version
# must supply its own guidance; it must never inherit stale verification claims.
if (!(Test-Path -LiteralPath $documentDirectory -PathType Container) -and $Version -ceq '1.0') {
    $documentDirectory = Join-Path $PSScriptRoot 'Release'
}
foreach ($document in $documents) {
    $documentPath = Join-Path $documentDirectory $document
    if (!(Test-Path -LiteralPath $documentPath -PathType Leaf)) { throw "Missing $Version release document: $document" }
    Assert-PlainPath $documentPath
}
# Verify/copy original notices into the isolated candidate before shipping it.
$notices = @(& (Join-Path $PSScriptRoot 'Copy-PocketBumpReleaseNotices.ps1') -BuildDirectory $candidate -PlayerName PocketBump)
if ($notices.Count -ne 44) { throw 'All 44 third-party notice and source-record files are required.' }
$files = @($candidateManifest.files | ForEach-Object { Get-Item -LiteralPath (Join-Path $candidate $_.path) -Force }) +
    @($notices | ForEach-Object { Get-Item -LiteralPath $_.Destination -Force })
foreach ($file in $files) { Assert-PlainPath $file.FullName }
foreach ($document in $documents) {
    if ($files.Name -contains $document) { throw "Candidate already contains reserved package document: $document" }
}
if ($files.Name -contains 'SHA256SUMS.txt') { throw 'Candidate already contains a reserved package manifest.' }
# New destination only: every copy is non-overwriting and byte-verified.
$null = New-Item -ItemType Directory -Path $destination
$manifest = [Collections.Generic.List[string]]::new()
foreach ($file in $files) {
    $relative = $file.FullName.Substring($candidate.Length + 1)
    $target = Join-Path $destination $relative
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.File]::Copy($file.FullName, $target, $false)
    $hash = (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash
    if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -cne $hash) { throw "Copy hash mismatch: $relative" }
    $manifest.Add($hash + '  ' + $relative.Replace('\', '/'))
}
# Bind the delivered scene/assets/native libraries as well as managed code to acceptance.
# This also catches a candidate changing between preflight and its copy.
$null = Assert-PocketBumpReleaseCandidateManifest -CandidateDirectory $destination `
    -ExpectedFiles @($acceptance.candidate_files) -ExpectedManifestSha256 $acceptance.candidate_manifest_sha256
foreach ($document in $documents) {
    $target = Join-Path $destination $document
    [IO.File]::Copy((Join-Path $documentDirectory $document), $target, $false)
    $manifest.Add((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash + '  ' + $document)
}
[IO.File]::WriteAllLines((Join-Path $destination 'SHA256SUMS.txt'), @($manifest | Sort-Object), [Text.UTF8Encoding]::new($false))
Add-Type -AssemblyName System.IO.Compression.FileSystem
New-PocketBumpReleaseZip -SourceDirectory $destination -ZipPath $zipPath
$archive = [IO.Compression.ZipFile]::OpenRead($zipPath)
try {
    if (@($archive.Entries | Where-Object { $_.Name }).Count -ne $manifest.Count + 1) { throw 'Archive file count does not match delivered package.' }
    $expectedZipFiles = [Collections.Generic.Dictionary[string,string]]::new([StringComparer]::Ordinal)
    foreach ($line in $manifest) { $expectedZipFiles.Add($line.Substring(66), $line.Substring(0,64)) }
    $expectedZipFiles.Add('SHA256SUMS.txt', (Get-FileHash -LiteralPath (Join-Path $destination 'SHA256SUMS.txt') -Algorithm SHA256).Hash)
    foreach ($relative in $expectedZipFiles.Keys) {
        $entry = $archive.GetEntry($packageName + '/' + $relative)
        if ($null -eq $entry) { throw "Archive lacks the expected file: $relative" }
        $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
        try { $entryHash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $stream.Dispose(); $sha.Dispose() }
        if ($entryHash -cne $expectedZipFiles[$relative]) { throw "Archived file hash mismatch: $relative" }
    }
} finally { $archive.Dispose() }
[pscustomobject]@{
    directory=$destination; zip=$zipPath; version=$identity.version; build_guid=$identity.build_guid;
    files=$manifest.Count + 1; notices=$notices.Count;
    accepted_candidate_manifest_sha256=$candidateManifest.manifest_sha256;
    zip_sha256=(Get-FileHash -LiteralPath $zipPath -Algorithm SHA256).Hash;
    physical_two_computer_validated=$false
} | ConvertTo-Json
