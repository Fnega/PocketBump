param([switch]$RequireReparseFixture)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'ReleaseCandidateManifest.ps1')
$fixtureParent = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../.art-source/test-fixtures')).TrimEnd([char[]]'\/')
$null = [IO.Directory]::CreateDirectory($fixtureParent)
$fixtureRoot = Join-Path $fixtureParent ('PocketBumpReleaseManifest-' + [Guid]::NewGuid().ToString('N'))
$candidate = Join-Path $fixtureRoot 'Candidate'
$null = [IO.Directory]::CreateDirectory($candidate)
$checks = 0
$skipped = @()
function Assert-True([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw "Manifest test failed: $Label" }
    $script:checks++
}
function Put-Fixture([string]$Relative, [string]$Value) {
    $target = Join-Path $candidate $Relative
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))
    [IO.File]::WriteAllText($target, $Value, [Text.UTF8Encoding]::new($false))
}
function Assert-Rejected([string]$Label, [scriptblock]$Action) {
    $rejected = $false
    try { & $Action > $null } catch { $rejected = $true }
    Assert-True $rejected $Label
}
try {
    Put-Fixture 'PocketBump.exe' 'exe1'
    Put-Fixture 'UnityPlayer.dll' 'native1'
    Put-Fixture 'PocketBump_Data/Managed/PocketBump.Runtime.dll' 'managed1'
    Put-Fixture 'PocketBump_Data/level0' 'scene1'
    Put-Fixture 'PocketBump_Data/sharedassets0.assets' 'assets1'
    Put-Fixture 'build-info.json' '{"version":"1.0"}'
    Put-Fixture 'ThirdPartyNotices/notice.txt' 'notice1'
    Put-Fixture 'PocketBump_BurstDebugInformation_DoNotShip/debug.txt' 'debug1'
    Put-Fixture 'WinPixEventRuntime.dll' 'excluded1'
    $baseline = Get-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidate
    Assert-True ($baseline.files.Count -eq 6) 'only the six shipped payload files are bound'
    $null = Assert-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidate -ExpectedFiles $baseline.files -ExpectedManifestSha256 $baseline.manifest_sha256
    Assert-True $true 'unchanged candidate accepted'
    $jsonRoundTrip = $baseline | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $null = Assert-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidate -ExpectedFiles @($jsonRoundTrip.files) -ExpectedManifestSha256 $jsonRoundTrip.manifest_sha256
    Assert-True $true 'manifest survives acceptance JSON serialization'
    foreach ($relative in @('PocketBump_Data/level0','PocketBump_Data/sharedassets0.assets','PocketBump_Data/Managed/PocketBump.Runtime.dll','UnityPlayer.dll')) {
        $target = Join-Path $candidate $relative
        $original = [IO.File]::ReadAllText($target)
        Put-Fixture $relative ('X' + $original.Substring(1))
        Assert-Rejected "changed payload rejected: $relative" {
            Assert-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidate -ExpectedFiles $baseline.files -ExpectedManifestSha256 $baseline.manifest_sha256
        }
        Put-Fixture $relative $original
    }
    $asset = Join-Path $candidate 'PocketBump_Data/level0'
    Remove-Item -LiteralPath $asset
    Assert-Rejected 'missing scene rejected' {
        Assert-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidate -ExpectedFiles $baseline.files -ExpectedManifestSha256 $baseline.manifest_sha256
    }
    Put-Fixture 'PocketBump_Data/level0' 'scene1'
    Put-Fixture 'PocketBump_Data/unexpected.asset' 'extra'
    Assert-Rejected 'extra payload rejected' {
        Assert-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidate -ExpectedFiles $baseline.files -ExpectedManifestSha256 $baseline.manifest_sha256
    }
    Remove-Item -LiteralPath (Join-Path $candidate 'PocketBump_Data/unexpected.asset')
    Put-Fixture 'ThirdPartyNotices/notice.txt' 'updated independently'
    Put-Fixture 'PocketBump_BurstDebugInformation_DoNotShip/debug.txt' 'updated debug'
    Put-Fixture 'WinPixEventRuntime.dll' 'updated excluded'
    $null = Assert-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidate -ExpectedFiles $baseline.files -ExpectedManifestSha256 $baseline.manifest_sha256
    Assert-True $true 'non-shipped debug files and separately verified notices do not alter payload identity'
    Assert-Rejected 'wrong aggregate checksum rejected' {
        Assert-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidate -ExpectedFiles $baseline.files -ExpectedManifestSha256 ('0' * 64)
    }
    $tampered = $baseline | ConvertTo-Json -Depth 5 | ConvertFrom-Json
    $tampered.files[0].length++
    Assert-Rejected 'tampered expected manifest rejected' {
        Assert-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidate -ExpectedFiles @($tampered.files) -ExpectedManifestSha256 $baseline.manifest_sha256
    }
    Assert-Rejected 'duplicate manifest paths rejected' {
        Get-PocketBumpReleaseManifestHash -Files @($baseline.files[0], $baseline.files[0])
    }
    $junctionTarget = Join-Path $fixtureRoot 'JunctionTarget'
    $null = [IO.Directory]::CreateDirectory($junctionTarget)
    $junctionPath = Join-Path $candidate 'ForbiddenJunction'
    $junctionCreated = $false
    try { $null = New-Item -ItemType Junction -Path $junctionPath -Target $junctionTarget; $junctionCreated = $true }
    catch {
        $accessDenied = $_.Exception -is [UnauthorizedAccessException] -or
            ($_.Exception -is [ComponentModel.Win32Exception] -and $_.Exception.NativeErrorCode -eq 5)
        if ($RequireReparseFixture -or !$accessDenied) { throw }
        $skipped += 'Junction fixture creation was denied by this environment; production rejection logic remains enabled.'
    }
    if ($junctionCreated) {
        try {
            Assert-Rejected 'junction rejected before traversal' {
                Get-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidate
            }
        } finally { Remove-Item -LiteralPath $junctionPath -Force }
    }
    [pscustomobject]@{
        passed=$true; checks=$checks; skipped=$skipped;
        scope='isolated owned manifest fixtures'; schema=$baseline.schema
    } | ConvertTo-Json
} finally {
    $resolvedFixture = [IO.Path]::GetFullPath($fixtureRoot).TrimEnd([char[]]'\/')
    if (!$resolvedFixture.StartsWith($fixtureParent + '\', [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedFixture) -notmatch '^PocketBumpReleaseManifest-[0-9a-f]{32}$') {
        throw 'Refusing to clean an unverified fixture path.'
    }
    if (Test-Path -LiteralPath $resolvedFixture) { Remove-Item -LiteralPath $resolvedFixture -Recurse -Force }
}
