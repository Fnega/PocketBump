[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) {
    throw 'Run this fixture with Windows PowerShell 5.1 (powershell.exe).'
}
. (Join-Path $PSScriptRoot 'ReleaseCandidateManifest.ps1')
Add-Type -AssemblyName System.IO.Compression.FileSystem

# Synthetic, never-executed EXEs and payloads test packaging contracts only.
# Nothing here is evidence that a real Unity player passed gameplay acceptance.
$fixtureParent = Join-Path $PSScriptRoot 'ReleaseResults'
$null = [IO.Directory]::CreateDirectory($fixtureParent)
$fixtureRoot = Join-Path $fixtureParent ('ReleasePackageFixture-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($fixtureRoot)
$checks = 0
$utf8 = [Text.UTF8Encoding]::new($false)
$packager = Join-Path $PSScriptRoot 'New-PocketBumpReleasePackage.ps1'

function Assert-PackageTrue([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw "Release package fixture failed: $Label" }
    $script:checks++
}
function Assert-PackageRejected([scriptblock]$Action, [string]$Reason, [string]$Label) {
    $message = ''
    try { & $Action | Out-Null } catch { $message = $_.Exception.Message }
    Assert-PackageTrue ($message -like ('*' + $Reason + '*')) ($Label + ': ' + $message)
}
function New-PackageFixtureDirectory([string]$Relative) {
    $path = [IO.Path]::GetFullPath((Join-Path $fixtureRoot $Relative))
    if (!$path.StartsWith($fixtureRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture path escaped its owned directory.' }
    $null = [IO.Directory]::CreateDirectory($path)
    return $path
}
function Write-PackageFixtureText([string]$Path, [string]$Text) {
    $path = [IO.Path]::GetFullPath($Path)
    if (!$path.StartsWith($fixtureRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture file escaped its owned directory.' }
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path))
    [IO.File]::WriteAllText($path, $Text, $utf8)
}
function New-PackageFixtureCandidate([string]$Version) {
    $directory = New-PackageFixtureDirectory ('candidate-' + $Version)
    $exe = Join-Path $directory 'PocketBump.exe'
    $typeName = 'Fixture' + [Guid]::NewGuid().ToString('N')
    $code = @"
using System.Reflection;
[assembly: AssemblyCompany("Fnega")]
[assembly: AssemblyProduct("PocketBump")]
[assembly: AssemblyTitle("射球大战 / PocketBump")]
[assembly: AssemblyVersion("$Version.0.0")]
[assembly: AssemblyFileVersion("$Version.0.0")]
[assembly: AssemblyInformationalVersion("$Version")]
internal static class $typeName { private static void Main() {} }
"@
    Add-Type -TypeDefinition $code -OutputAssembly $exe -OutputType ConsoleApplication
    Write-PackageFixtureText (Join-Path $directory 'UnityPlayer.dll') 'synthetic native fixture'
    Write-PackageFixtureText (Join-Path $directory 'PocketBump_Data/Managed/PocketBump.Runtime.dll') 'synthetic runtime fixture'
    Write-PackageFixtureText (Join-Path $directory 'PocketBump_Data/Resources/场景 with spaces.bin') 'synthetic scene fixture'
    $identity = [pscustomobject]@{
        game='射球大战 / PocketBump'; developer='Fnega'; version=$Version; development=$false;
        build_guid=[Guid]::NewGuid().ToString('N'); protocol=$(if ($Version -ceq '1.0') { 7 } else { 8 })
    }
    $identityPath = Join-Path $directory 'build-info.json'
    $identityJson = $identity | ConvertTo-Json
    Write-PackageFixtureText $identityPath $identityJson
    $manifest = Get-PocketBumpReleaseCandidateManifest -CandidateDirectory $directory
    $acceptance = [pscustomobject]@{
        schema='pocketbump_release_ui_acceptance_v1'; candidate_manifest_schema=$manifest.schema;
        passed=$true; expected_version=$Version; expected_build_guid=$identity.build_guid;
        player_sha256=(Get-FileHash -LiteralPath $exe -Algorithm SHA256).Hash;
        runtime_assembly_sha256=(Get-FileHash -LiteralPath (Join-Path $directory 'PocketBump_Data/Managed/PocketBump.Runtime.dll') -Algorithm SHA256).Hash;
        build_info_sha256=(Get-FileHash -LiteralPath $identityPath -Algorithm SHA256).Hash;
        candidate_files=@($manifest.files); candidate_manifest_sha256=$manifest.manifest_sha256
    }
    $acceptancePath = Join-Path $fixtureRoot ('acceptance-' + $Version + '.json')
    $acceptanceJson = $acceptance | ConvertTo-Json -Depth 8
    Write-PackageFixtureText $acceptancePath $acceptanceJson
    [pscustomobject]@{
        directory=$directory; acceptance_path=$acceptancePath; acceptance_json=$acceptanceJson;
        identity_path=$identityPath; identity_json=$identityJson; manifest=$manifest
    }
}
function Test-PackageFixtureOutput($Candidate, $Output, [string]$Version, [string]$DocumentDirectory) {
    Assert-PackageTrue ($Output.version -ceq $Version) 'output version matches requested version'
    Assert-PackageTrue ($Output.notices -eq 44) 'all original 44 notices and source records are present'
    Assert-PackageTrue ($Output.files -eq $Candidate.manifest.files.Count + 48) 'payload, 44 notices, three documents and checksum are counted'
    Assert-PackageTrue ($Output.physical_two_computer_validated -eq $false) 'packaging never claims physical two-computer validation'
    Assert-PackageTrue ($Output.accepted_candidate_manifest_sha256 -ceq $Candidate.manifest.manifest_sha256) 'complete acceptance manifest stays bound'
    Assert-PackageTrue ((Get-PocketBumpReleaseCandidateManifest -CandidateDirectory $Candidate.directory).manifest_sha256 -ceq $Candidate.manifest.manifest_sha256) 'candidate game payload is unchanged'
    foreach ($name in @('试玩说明.txt', '双电脑验收.txt', 'release-verification.txt')) {
        Assert-PackageTrue ((Get-FileHash -LiteralPath (Join-Path $Output.directory $name) -Algorithm SHA256).Hash -ceq
            (Get-FileHash -LiteralPath (Join-Path $DocumentDirectory $name) -Algorithm SHA256).Hash) 'version-specific document source is copied verbatim'
    }
    $lines = @(Get-Content -LiteralPath (Join-Path $Output.directory 'SHA256SUMS.txt') -Encoding UTF8)
    Assert-PackageTrue ($lines.Count -eq $Output.files - 1) 'checksum list covers every non-manifest delivery file'
    $archive = [IO.Compression.ZipFile]::OpenRead($Output.zip)
    try {
        Assert-PackageTrue ($archive.Entries.Count -eq $Output.files) 'ZIP contains exactly the delivered files'
        foreach ($line in $lines) {
            $relative = $line.Substring(66)
            Assert-PackageTrue (!$relative.Contains('\')) 'checksum manifest uses forward slashes'
            $entry = $archive.GetEntry('PocketBump' + $Version + '/' + $relative)
            Assert-PackageTrue ($null -ne $entry) 'ZIP uses exactly the selected version root and forward slashes'
            $stream = $entry.Open(); $sha = [Security.Cryptography.SHA256]::Create()
            try { $hash = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
            finally { $stream.Dispose(); $sha.Dispose() }
            Assert-PackageTrue ($hash -ceq $line.Substring(0, 64)) 'every archived file matches its checksum'
        }
    } finally { $archive.Dispose() }
}

try {
    $legacy = New-PackageFixtureCandidate '1.0'
    $previous = New-PackageFixtureCandidate '1.1'
    $current = New-PackageFixtureCandidate '1.2'
    $delivery10 = Join-Path $fixtureRoot 'PocketBump1.0'
    $delivery11 = Join-Path $fixtureRoot 'PocketBump1.1'
    $delivery12 = Join-Path $fixtureRoot 'PocketBump1.2'
    $legacyOutput = & $packager -CandidateDirectory $legacy.directory -DestinationDirectory $delivery10 -AcceptancePath $legacy.acceptance_path -Version '1.0' | ConvertFrom-Json
    Test-PackageFixtureOutput $legacy $legacyOutput '1.0' (Join-Path $PSScriptRoot 'Release')
    $legacyZipHash = (Get-FileHash -LiteralPath $legacyOutput.zip -Algorithm SHA256).Hash
    $legacyManifestHash = (Get-FileHash -LiteralPath (Join-Path $delivery10 'SHA256SUMS.txt') -Algorithm SHA256).Hash
    $previousOutput = & $packager -CandidateDirectory $previous.directory -DestinationDirectory $delivery11 -AcceptancePath $previous.acceptance_path -Version '1.1' | ConvertFrom-Json
    Test-PackageFixtureOutput $previous $previousOutput '1.1' (Join-Path $PSScriptRoot 'Release/1.1')
    $previousZipHash = (Get-FileHash -LiteralPath $previousOutput.zip -Algorithm SHA256).Hash
    $previousManifestHash = (Get-FileHash -LiteralPath (Join-Path $delivery11 'SHA256SUMS.txt') -Algorithm SHA256).Hash

    Assert-PackageRejected { & $packager -CandidateDirectory $current.directory -DestinationDirectory $delivery12 -AcceptancePath $current.acceptance_path -Version '1.3' } 'ValidateSet' 'unsupported version fails parameter validation'
    Assert-PackageRejected { & $packager -CandidateDirectory $current.directory -DestinationDirectory (Join-Path $fixtureRoot 'PocketBump1.20') -AcceptancePath $current.acceptance_path } 'must be named PocketBump1.2' 'folder name cannot use a prefix or alias'
    Assert-PackageRejected { & $packager -CandidateDirectory $legacy.directory -DestinationDirectory $delivery12 -AcceptancePath $legacy.acceptance_path } 'non-Development 1.2' '1.0 candidate cannot be relabeled as 1.2'
    Assert-PackageRejected { & $packager -CandidateDirectory $previous.directory -DestinationDirectory $delivery12 -AcceptancePath $previous.acceptance_path } 'non-Development 1.2' '1.1 candidate cannot be relabeled as 1.2'
    foreach ($field in @('expected_version', 'expected_build_guid', 'player_sha256', 'runtime_assembly_sha256', 'build_info_sha256', 'passed')) {
        $altered = $current.acceptance_json | ConvertFrom-Json
        switch ($field) {
            'expected_version' { $altered.$field = '1.1' }
            'expected_build_guid' { $altered.$field = [Guid]::NewGuid().ToString('N') }
            'passed' { $altered.$field = $false }
            default { $altered.$field = '0' * 64 }
        }
        Write-PackageFixtureText $current.acceptance_path ($altered | ConvertTo-Json -Depth 8)
        Assert-PackageRejected { & $packager -CandidateDirectory $current.directory -DestinationDirectory $delivery12 -AcceptancePath $current.acceptance_path } 'matching these exact candidate bits' ('mismatched acceptance ' + $field + ' is rejected')
        Assert-PackageTrue (!(Test-Path -LiteralPath $delivery12)) 'failed acceptance creates no delivery directory'
    }
    Write-PackageFixtureText $current.acceptance_path $current.acceptance_json
    foreach ($field in @('protocol', 'development')) {
        $altered = $current.identity_json | ConvertFrom-Json
        if ($field -eq 'protocol') { $altered.protocol = 7 } else { $altered.development = $true }
        Write-PackageFixtureText $current.identity_path ($altered | ConvertTo-Json)
        Assert-PackageRejected { & $packager -CandidateDirectory $current.directory -DestinationDirectory $delivery12 -AcceptancePath $current.acceptance_path } 'non-Development 1.2' ('incorrect build ' + $field + ' is rejected')
    }
    Write-PackageFixtureText $current.identity_path $current.identity_json

    $scene = Join-Path $current.directory 'PocketBump_Data/Resources/场景 with spaces.bin'
    Write-PackageFixtureText $scene 'changed scene fixture'
    Assert-PackageRejected { & $packager -CandidateDirectory $current.directory -DestinationDirectory $delivery12 -AcceptancePath $current.acceptance_path } 'Candidate payload differs' 'non-code scene changes invalidate the complete acceptance manifest'
    Write-PackageFixtureText $scene 'synthetic scene fixture'
    $altered = $current.acceptance_json | ConvertFrom-Json
    $altered.candidate_manifest_sha256 = '0' * 64
    Write-PackageFixtureText $current.acceptance_path ($altered | ConvertTo-Json -Depth 8)
    Assert-PackageRejected { & $packager -CandidateDirectory $current.directory -DestinationDirectory $delivery12 -AcceptancePath $current.acceptance_path } 'own checksum is invalid' 'forged acceptance manifest is rejected'
    Write-PackageFixtureText $current.acceptance_path $current.acceptance_json
    Assert-PackageTrue (!(Test-Path -LiteralPath $delivery12) -and !(Test-Path -LiteralPath ($delivery12 + '.zip'))) 'all failed preflights leave new delivery targets absent'

    # Omit Version deliberately: the approved current default is 1.2.
    $currentOutput = & $packager -CandidateDirectory $current.directory -DestinationDirectory $delivery12 -AcceptancePath $current.acceptance_path | ConvertFrom-Json
    Test-PackageFixtureOutput $current $currentOutput '1.2' (Join-Path $PSScriptRoot 'Release/1.2')
    Assert-PackageTrue ((Get-FileHash -LiteralPath $legacyOutput.zip -Algorithm SHA256).Hash -ceq $legacyZipHash) 'new 1.2 packaging preserves existing 1.0 ZIP bytes'
    Assert-PackageTrue ((Get-FileHash -LiteralPath (Join-Path $delivery10 'SHA256SUMS.txt') -Algorithm SHA256).Hash -ceq $legacyManifestHash) 'new 1.2 packaging preserves existing 1.0 package manifest'
    Assert-PackageTrue ((Get-FileHash -LiteralPath $previousOutput.zip -Algorithm SHA256).Hash -ceq $previousZipHash) 'new 1.2 packaging preserves existing 1.1 ZIP bytes'
    Assert-PackageTrue ((Get-FileHash -LiteralPath (Join-Path $delivery11 'SHA256SUMS.txt') -Algorithm SHA256).Hash -ceq $previousManifestHash) 'new 1.2 packaging preserves existing 1.1 package manifest'
    Assert-PackageRejected { & $packager -CandidateDirectory $current.directory -DestinationDirectory $delivery12 -AcceptancePath $current.acceptance_path } 'already exists' 'existing 1.2 output cannot be overwritten'
    Assert-PackageTrue ((Get-FileHash -LiteralPath $currentOutput.zip -Algorithm SHA256).Hash -ceq $currentOutput.zip_sha256) 'rejected overwrite preserves existing 1.2 ZIP'
    [pscustomobject]@{
        status='passed'; checks=$checks; skipped=@(); powershell_version=$PSVersionTable.PSVersion.ToString(); fixture=$fixtureRoot;
        scope='Synthetic package contract fixtures; no real release acceptance or final delivery performed.';
        cleanup='None: named project-local fixture retained for audit; existing releases and reports were untouched.'
    } | ConvertTo-Json -Depth 4
} catch {
    Write-Warning "Failed fixture retained for audit: $fixtureRoot"
    throw
}
