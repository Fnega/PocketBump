[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$BuildDirectory,
    [ValidateSet('PhysicscollGame', 'PocketBump')]
    [string]$PlayerName = 'PhysicscollGame'
)

$ErrorActionPreference = 'Stop'

function Assert-PlainDirectoryAncestors([string]$Directory) {
    $current = $Directory
    while (![string]::IsNullOrEmpty($current)) {
        if (Test-Path -LiteralPath $current) {
            $item = Get-Item -LiteralPath $current -Force
            if (!$item.PSIsContainer -or ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
                throw "Directory path contains a file or reparse point: $current"
            }
        }
        $current = [IO.Path]::GetDirectoryName($current)
    }
}

function Get-PlainFileHash([string]$FilePath) {
    if (!(Test-Path -LiteralPath $FilePath -PathType Leaf)) { throw "Required file is missing: $FilePath" }
    $item = Get-Item -LiteralPath $FilePath -Force
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "File is a reparse point: $FilePath" }
    Assert-PlainDirectoryAncestors ([IO.Path]::GetDirectoryName($FilePath))
    return (Get-FileHash -LiteralPath $FilePath -Algorithm SHA256).Hash
}

# IsPathRooted alone also accepts drive-relative C:folder and root-relative \folder.
if ($BuildDirectory -notmatch '^(?:[A-Za-z]:[\\/]|\\\\[^\\/]+[\\/][^\\/]+[\\/])') {
    throw 'BuildDirectory must be a fully qualified absolute Windows filesystem path.'
}
$buildRoot = [IO.Path]::GetFullPath($BuildDirectory).TrimEnd([char[]]'\/')
if ($buildRoot -eq [IO.Path]::GetPathRoot($buildRoot).TrimEnd([char[]]'\/') -or
    !(Test-Path -LiteralPath $buildRoot -PathType Container)) {
    throw "BuildDirectory must identify an existing built-player directory, not a filesystem root: $BuildDirectory"
}
Assert-PlainDirectoryAncestors $buildRoot
$buildRoot = (Get-Item -LiteralPath $buildRoot -Force).FullName.TrimEnd([char[]]'\/')
$null = Get-PlainFileHash (Join-Path $buildRoot ($PlayerName + '.exe'))
$dataDirectory = Join-Path $buildRoot ($PlayerName + '_Data')
if (!(Test-Path -LiteralPath $dataDirectory -PathType Container)) { throw "Built-player data directory is missing: $dataDirectory" }
Assert-PlainDirectoryAncestors $dataDirectory

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$noticesRoot = Join-Path $buildRoot 'ThirdPartyNotices'
$mappings = @(
    @('Assets/PocketBumpDashArt/Fonts/LICENSE.txt', 'NotoSansCJK/LICENSE.txt'),
    @('Assets/PocketBumpDashArt/Fonts/THIRD_PARTY_NOTICES.md', 'NotoSansCJK/THIRD_PARTY_NOTICES.md'),
    @('Assets/TextMesh Pro/Fonts/LiberationSans - OFL.txt', 'LiberationSans/LiberationSans - OFL.txt'),
    @('Assets/Resources/PocketBump/Art/Kenney/THIRD_PARTY_NOTICES.md', 'Kenney/THIRD_PARTY_NOTICES.md'),
    @('Assets/Resources/PocketBump/Art/Kenney/Licenses/InputPrompts.txt', 'Kenney/Licenses/InputPrompts.txt'),
    @('Assets/Resources/PocketBump/Art/Kenney/Licenses/LightMasks.txt', 'Kenney/Licenses/LightMasks.txt'),
    @('Assets/Resources/PocketBump/Art/Kenney/Licenses/MarbleKit.txt', 'Kenney/Licenses/MarbleKit.txt'),
    @('Assets/Resources/PocketBump/Art/Kenney/Licenses/ModularSpaceKit.txt', 'Kenney/Licenses/ModularSpaceKit.txt'),
    @('Assets/Resources/PocketBump/Art/Kenney/Licenses/ParticlePack.txt', 'Kenney/Licenses/ParticlePack.txt'),
    @('Assets/Resources/PocketBump/Art/Kenney/Licenses/PatternPackLines.txt', 'Kenney/Licenses/PatternPackLines.txt'),
    @('Assets/Resources/PocketBump/Art/Kenney/Licenses/UiPackSciFi.txt', 'Kenney/Licenses/UiPackSciFi.txt')
)
# Frozen 1.0 runtime-package inventory, including the native collab-proxy library
# actually present in the candidate. Missing original notices fail preflight.
$runtimePackages = @(
    'com.unity.2d.animation@9.2.0', 'com.unity.2d.common@8.1.0',
    'com.unity.2d.pixel-perfect@5.1.0', 'com.unity.2d.spriteshape@9.1.0',
    'com.unity.2d.tilemap.extras@3.1.3', 'com.unity.burst@1.8.21',
    'com.unity.collab-proxy@2.12.4', 'com.unity.collections@1.2.4',
    'com.unity.mathematics@1.2.6', 'com.unity.nuget.newtonsoft-json@3.2.1',
    'com.unity.render-pipelines.core@14.0.12', 'com.unity.render-pipelines.universal@14.0.12',
    'com.unity.render-pipelines.universal-config@14.0.10', 'com.unity.shadergraph@14.0.12',
    'com.unity.textmeshpro@3.0.7', 'com.unity.timeline@1.7.7',
    'com.unity.ugui@1.0.0', 'com.unity.visualscripting@1.9.4'
)
$packagesWithThirdPartyNotices = @(
    'com.unity.2d.animation@9.2.0', 'com.unity.2d.common@8.1.0',
    'com.unity.2d.spriteshape@9.1.0', 'com.unity.2d.tilemap.extras@3.1.3',
    'com.unity.burst@1.8.21', 'com.unity.collab-proxy@2.12.4',
    'com.unity.nuget.newtonsoft-json@3.2.1', 'com.unity.visualscripting@1.9.4'
)
$lockPath = Join-Path $repositoryRoot 'Packages/packages-lock.json'
$null = Get-PlainFileHash $lockPath
$locked = (Get-Content -LiteralPath $lockPath -Raw -Encoding UTF8 | ConvertFrom-Json).dependencies
foreach ($package in $runtimePackages) {
    $parts = $package.Split('@')
    if ($locked.($parts[0]).version -cne $parts[1]) { throw "Runtime notice version no longer matches packages-lock.json: $package" }
    $mappings += ,@("Library/PackageCache/$package/LICENSE.md", "RuntimePackages/$package/LICENSE.md")
    if ($packagesWithThirdPartyNotices -contains $package) {
        $mappings += ,@("Library/PackageCache/$package/Third Party Notices.md", "RuntimePackages/$package/Third Party Notices.md")
    }
}
$mappings += ,@('Library/PackageCache/com.unity.mathematics@1.2.6/Unity.Mathematics/Noise/LICENSE', 'RuntimePackages/com.unity.mathematics@1.2.6/Noise-LICENSE.txt')
$mappings += ,@('Library/PackageCache/com.unity.render-pipelines.universal@14.0.12/Runtime/External/LibTessDotNet/LICENSE.txt', 'RuntimePackages/com.unity.render-pipelines.universal@14.0.12/LibTessDotNet-LICENSE.txt')
$mappings += ,@('Library/PackageCache/com.unity.2d.spriteshape@9.1.0/Runtime/External/LibTessDotNet/LICENSE.txt', 'RuntimePackages/com.unity.2d.spriteshape@9.1.0/LibTessDotNet-LICENSE.txt')
$mcpCommit = '4ce7dd3cc54e37e2ed6dc59cb5a047f3dccb3f50'
if ($locked.'com.coplaydev.unity-mcp'.hash -cne $mcpCommit) { throw 'MCPForUnity notice no longer matches the locked source commit.' }
$mappings += ,@('Tools/ReleaseNotices/RuntimePackages-SOURCES.txt', 'RuntimePackages/SOURCES.txt')
$mappings += ,@("Tools/ReleaseNotices/MCPForUnity-$mcpCommit-LICENSE.txt", 'RuntimePackages/MCPForUnity/LICENSE.txt')
if ((Get-PlainFileHash (Join-Path $repositoryRoot $mappings[-1][0])) -cne '6EFE650C965012AC418238DCD6B9116E4130A5220717EF0DFB539DD159C4245C') {
    throw 'The pinned MCPForUnity original license bytes have changed.'
}
$onigNotices = @(
    @('Onigwrap-1.0.10-LICENSE.txt', 'LICENSE.txt', '4DC9331502C288D277EC8AABE9322F833E9507C9B123AC95086CA44088616E38'),
    @('Onigwrap-1.0.10-THIRD-PARTY-NOTICES.txt', 'THIRD-PARTY-NOTICES.txt', '5662CC07E23C65FD5D3790D9B8A4D7113F1DE881DE150A0E93296A2FAA1418A9')
)
foreach ($onigNotice in $onigNotices) {
    $relativeSource = 'Tools/ReleaseNotices/' + $onigNotice[0]
    if ((Get-PlainFileHash (Join-Path $repositoryRoot $relativeSource)) -cne $onigNotice[2]) {
        throw "The pinned Onigwrap 1.0.10 original notice bytes have changed: $relativeSource"
    }
    $mappings += ,@($relativeSource, ('RuntimePackages/Onigwrap-1.0.10/' + $onigNotice[1]))
}

# Inspect every source and destination before creating any directory or copying a file.
# No caller-controlled source path, downloads, transcoding, or existing-file overwrite.
$plan = foreach ($mapping in $mappings) {
    $source = [IO.Path]::GetFullPath((Join-Path $repositoryRoot $mapping[0]))
    $destination = [IO.Path]::GetFullPath((Join-Path $noticesRoot $mapping[1]))
    if (!$destination.StartsWith($buildRoot + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
        throw "Notice destination escapes the selected build: $destination"
    }
    $hash = Get-PlainFileHash $source
    Assert-PlainDirectoryAncestors ([IO.Path]::GetDirectoryName($destination))
    $exists = Test-Path -LiteralPath $destination
    if ($exists -and (Get-PlainFileHash $destination) -cne $hash) {
        throw "Existing notice differs from its source; nothing was copied: $destination"
    }
    [pscustomobject]@{ Source = $source; SourceRelativePath = $mapping[0]; Destination = $destination; RelativePath = $mapping[1]; SHA256 = $hash; Exists = $exists }
}

$results = foreach ($entry in $plan) {
    if (!$entry.Exists) {
        $parentDirectory = [IO.Path]::GetDirectoryName($entry.Destination)
        Assert-PlainDirectoryAncestors $parentDirectory
        $null = [IO.Directory]::CreateDirectory($parentDirectory)
        # A destination appearing after preflight is an error, never an overwrite.
        [IO.File]::Copy($entry.Source, $entry.Destination, $false)
    }
    if ((Get-PlainFileHash $entry.Destination) -cne $entry.SHA256) {
        throw "Notice SHA256 verification failed: $($entry.Destination)"
    }
    [pscustomobject]@{
        RelativePath = $entry.RelativePath
        SourceRelativePath = $entry.SourceRelativePath
        SHA256 = $entry.SHA256
        Status = $(if ($entry.Exists) { 'VerifiedExisting' } else { 'CopiedAndVerified' })
        Destination = $entry.Destination
    }
}
$results
