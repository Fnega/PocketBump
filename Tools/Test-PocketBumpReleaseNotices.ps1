$ErrorActionPreference = 'Stop'
$copyScript = Join-Path $PSScriptRoot 'Copy-PocketBumpReleaseNotices.ps1'
$fixtureParent = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([char[]]'\/')
$fixtureRoot = Join-Path $fixtureParent ('PocketBumpReleaseNotices-' + [Guid]::NewGuid().ToString('N'))
$checks = 0

function Assert-True([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw "Notice fixture failed: $Label" }
    $script:checks++
}
function Assert-Rejected([scriptblock]$Action, [string]$Label) {
    $rejected = $false
    try { & $Action | Out-Null } catch { $rejected = $true }
    Assert-True $rejected $Label
}
function New-PlayerFixture([string]$Name, [string]$PlayerName = 'PhysicscollGame') {
    $directory = Join-Path $fixtureRoot $Name
    $null = New-Item -ItemType Directory -Path (Join-Path $directory ($PlayerName + '_Data'))
    $null = New-Item -ItemType File -Path (Join-Path $directory ($PlayerName + '.exe'))
    return $directory
}

$null = New-Item -ItemType Directory -Path $fixtureRoot
try {
    # Empty executable/data markers are synthetic packaging fixtures, not a valid game.
    $complete = New-PlayerFixture 'complete build with spaces'
    $release = New-PlayerFixture 'release PocketBump' 'PocketBump'
    $releaseNotices = @(& $copyScript -BuildDirectory $release -PlayerName PocketBump)
    Assert-True ($releaseNotices.Count -eq 44) 'renamed Release player copies all original notices and source record'
    $results = @(& $copyScript -BuildDirectory $complete)
    Assert-True ($results.Count -eq 44) 'all forty-four original notices and source record copied'
    $timestamps = @{}
    foreach ($item in $results) {
        Assert-True ($item.Status -ceq 'CopiedAndVerified') 'first invocation copies missing files'
        Assert-True ((Get-FileHash -LiteralPath $item.Destination -Algorithm SHA256).Hash -ceq $item.SHA256) 'copied bytes verify'
        $timestamps[$item.RelativePath] = (Get-Item -LiteralPath $item.Destination).LastWriteTimeUtc.Ticks
    }
    $again = @(& $copyScript -BuildDirectory $complete)
    Assert-True ($again.Count -eq 44) 'repeat verifies all files'
    foreach ($item in $again) {
        Assert-True ($item.Status -ceq 'VerifiedExisting') 'repeat does not overwrite existing files'
        Assert-True ((Get-Item -LiteralPath $item.Destination).LastWriteTimeUtc.Ticks -eq $timestamps[$item.RelativePath]) 'repeat preserves timestamps'
    }

    $conflict = New-PlayerFixture 'last notice conflicts'
    $conflictingFile = Join-Path $conflict 'ThirdPartyNotices/RuntimePackages/Onigwrap-1.0.10/THIRD-PARTY-NOTICES.txt'
    $null = New-Item -ItemType Directory -Path ([IO.Path]::GetDirectoryName($conflictingFile))
    [IO.File]::WriteAllBytes($conflictingFile, [Text.Encoding]::UTF8.GetBytes('Explicit conflicting test fixture, not a license.'))
    $conflictingHash = (Get-FileHash -LiteralPath $conflictingFile -Algorithm SHA256).Hash
    Assert-Rejected { & $copyScript -BuildDirectory $conflict } 'last-file conflict rejects the whole preflight'
    Assert-True (!(Test-Path -LiteralPath (Join-Path $conflict 'ThirdPartyNotices/NotoSansCJK'))) 'earlier notices were not partially copied'
    Assert-True ((Get-FileHash -LiteralPath $conflictingFile -Algorithm SHA256).Hash -ceq $conflictingHash) 'conflicting file remains unchanged'

    # An isolated replica of the script's fixed source layout tests a missing final
    # source without renaming/removing anything in the real repository.
    $sourceFixture = Join-Path $fixtureRoot 'incomplete source repository'
    $shadowScript = Join-Path $sourceFixture 'Tools/Copy-PocketBumpReleaseNotices.ps1'
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($shadowScript))
    [IO.File]::Copy($copyScript, $shadowScript, $false)
    $actualRepository = Split-Path -Parent $PSScriptRoot
    $missingSource = $results[-1].SourceRelativePath
    $sourceFiles = @('Packages/packages-lock.json') + @($results | Where-Object { $_.SourceRelativePath -cne $missingSource } | ForEach-Object { $_.SourceRelativePath })
    foreach ($relative in $sourceFiles) {
        $destination = Join-Path $sourceFixture $relative
        $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($destination))
        [IO.File]::Copy((Join-Path $actualRepository $relative), $destination, $false)
    }
    $missingSourceBuild = New-PlayerFixture 'missing source build'
    Assert-Rejected { & $shadowScript -BuildDirectory $missingSourceBuild } 'missing final source rejects full preflight'
    Assert-True (!(Test-Path -LiteralPath (Join-Path $missingSourceBuild 'ThirdPartyNotices'))) 'missing source cannot cause partial copying'

    $restoredSource = Join-Path $sourceFixture $missingSource
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($restoredSource))
    [IO.File]::Copy((Join-Path $actualRepository $missingSource), $restoredSource, $false)
    $shadowLockPath = Join-Path $sourceFixture 'Packages/packages-lock.json'
    $shadowLock = Get-Content -LiteralPath $shadowLockPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $shadowLock.dependencies.'com.unity.nuget.newtonsoft-json'.version = '0.0.0-fixture'
    [IO.File]::WriteAllText($shadowLockPath, ($shadowLock | ConvertTo-Json -Depth 20), [Text.UTF8Encoding]::new($false))
    Assert-Rejected { & $shadowScript -BuildDirectory $missingSourceBuild } 'notice inventory rejects a changed locked runtime version'
    Assert-True (!(Test-Path -LiteralPath (Join-Path $missingSourceBuild 'ThirdPartyNotices'))) 'version mismatch cannot cause partial copying'

    Assert-True (@($results | Where-Object { $_.RelativePath -eq 'RuntimePackages/com.unity.nuget.newtonsoft-json@3.2.1/Third Party Notices.md' }).Count -eq 1) 'shipped Newtonsoft DLL has its original notices'
    Assert-True (@($results | Where-Object { $_.RelativePath -eq 'RuntimePackages/com.unity.visualscripting@1.9.4/Third Party Notices.md' }).Count -eq 1) 'shipped ANTLR runtime has its original notices'

    $blocked = New-PlayerFixture 'parent is a file'
    $null = New-Item -ItemType File -Path (Join-Path $blocked 'ThirdPartyNotices')
    Assert-Rejected { & $copyScript -BuildDirectory $blocked } 'file in destination directory path rejected'
    Assert-Rejected { & $copyScript -BuildDirectory '.\relative build' } 'relative paths rejected'
    Assert-Rejected { & $copyScript -BuildDirectory 'D:drive-relative-build' } 'drive-relative paths rejected'
    Assert-Rejected { & $copyScript -BuildDirectory ([IO.Path]::GetPathRoot($fixtureRoot)) } 'filesystem roots rejected'
    Assert-Rejected { & $copyScript -BuildDirectory (Join-Path $fixtureRoot 'missing build') } 'missing build rejected'

    $incomplete = Join-Path $fixtureRoot 'not yet built'
    $null = New-Item -ItemType Directory -Path $incomplete
    Assert-Rejected { & $copyScript -BuildDirectory $incomplete } 'missing executable rejected'
    $null = New-Item -ItemType File -Path (Join-Path $incomplete 'PhysicscollGame.exe')
    Assert-Rejected { & $copyScript -BuildDirectory $incomplete } 'missing player data rejected'
    Assert-True (!(Test-Path -LiteralPath (Join-Path $incomplete 'ThirdPartyNotices'))) 'invalid build has no created notice directory'

    $linked = New-PlayerFixture 'linked notice directory'
    $outside = Join-Path $fixtureRoot 'outside selected fixture build'
    $null = New-Item -ItemType Directory -Path $outside
    $linkPath = Join-Path $linked 'ThirdPartyNotices'
    $null = New-Item -ItemType Junction -Path $linkPath -Target $outside
    try {
        Assert-Rejected { & $copyScript -BuildDirectory $linked } 'junction cannot redirect notices outside selected build'
        Assert-True (@(Get-ChildItem -LiteralPath $outside -Force).Count -eq 0) 'junction target remains empty'
    }
    finally {
        # Remove only the fixture junction, not its target or any real release directory.
        [IO.Directory]::Delete($linkPath)
    }
    Write-Output "PASS: $checks pure PowerShell release-notice checks; no real build was modified."
}
finally {
    # This exact GUID-named directory was created above. Resolve and bound it before
    # recursive cleanup; never enumerate arbitrary paths or delete the temp root.
    $resolvedFixture = (Resolve-Path -LiteralPath $fixtureRoot).ProviderPath.TrimEnd([char[]]'\/')
    if ($resolvedFixture -cne $fixtureRoot -or
        !$resolvedFixture.StartsWith($fixtureParent + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($resolvedFixture) -notmatch '^PocketBumpReleaseNotices-[0-9a-f]{32}$') {
        throw "Unsafe fixture cleanup path; refusing removal: $resolvedFixture"
    }
    Remove-Item -LiteralPath $resolvedFixture -Recurse -Force
}
