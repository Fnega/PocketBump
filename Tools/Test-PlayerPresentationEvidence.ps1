param([switch]$ValidateCurrent013Evidence)
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'PlayerPresentationEvidence.ps1')
Add-Type -AssemblyName System.Drawing
# Synthetic validator fixtures only. No Unity, Player, socket, or window operation.
$script:checks = 0
$fixtureName = 'PocketBump-PresentationEvidence-' + [guid]::NewGuid().ToString('N')
$fixtureRoot = Join-Path ([IO.Path]::GetTempPath()) $fixtureName
New-Item -ItemType Directory -Path $fixtureRoot | Out-Null
$script:fixtureIndex = 0
function New-ProbeFixture([int]$Frames = 600) {
    $script:fixtureIndex++
    $dir = Join-Path $fixtureRoot $script:fixtureIndex.ToString()
    New-Item -ItemType Directory -Path $dir | Out-Null
    $png = Join-Path $dir 'player-after-sampling.png'
    $bitmap = New-Object Drawing.Bitmap 64, 48
    try {
        for ($y = 0; $y -lt 48; $y++) {
            for ($x = 0; $x -lt 64; $x++) { $bitmap.SetPixel($x, $y, [Drawing.Color]::FromArgb(255, $x * 4, $y * 5, ($x + $y) * 2)) }
        }
        $bitmap.Save($png, [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
    $lines = @('sample,unscaled_frame_ms')
    for ($i = 0; $i -lt $Frames; $i++) { $lines += $i.ToString() + ',' + (4.0 + $i * 0.01).ToString('F6', [Globalization.CultureInfo]::InvariantCulture) }
    [IO.File]::WriteAllLines((Join-Path $dir 'frame-intervals.csv'), $lines)
    $report = [pscustomobject]@{
        evidence_kind = 'synthetic evidence-validator fixture, not Player acceptance'
        complete = $true; passed = $true; editor_run = $false
        Chinese_font_has_all_required_glyphs = $true; effect_capacity_bounds_held = $true
        errors = 0; presentation_errors = 0; requested_frames = $Frames; sampled_frames = $Frames
        width = 64; height = 48; realtime_start = 10; realtime_end = 20; tick_start = 100; tick_end = 700
        p50_frame_ms = 4.0 + ($Frames - 1) * 0.005
        p95_frame_ms = 4.0 + ($Frames - 1) * 0.0095
        max_frame_ms = 4.0 + ($Frames - 1) * 0.01
        screenshots = @($png)
    }
    [IO.File]::WriteAllText((Join-Path $dir 'player-presentation-probe.json'), ($report | ConvertTo-Json -Depth 4))
    return $dir
}
function Set-ProbeFixtureReport([string]$Directory, [scriptblock]$Mutate) {
    $path = Join-Path $Directory 'player-presentation-probe.json'
    $report = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json
    & $Mutate $report
    [IO.File]::WriteAllText($path, ($report | ConvertTo-Json -Depth 4))
}
function Assert-ProbeRejected([scriptblock]$Action, [string]$Label, [string]$ExpectedMessage) {
    $caught = $null
    try { $null = & $Action } catch { $caught = $_.Exception.Message }
    if ($null -eq $caught) { throw "Invalid synthetic presentation fixture accepted: $Label" }
    if ($ExpectedMessage -and !$caught.Contains($ExpectedMessage)) { throw "Wrong rejection for '$Label': $caught" }
    $script:checks++
}
function Assert-ProbeFixture([string]$Directory, [int]$Frames = 600, [switch]$RequireMinimap) {
    Assert-DashPlayerPresentationEvidence -Directory $Directory -ExpectedWidth 64 -ExpectedHeight 48 -ExpectedFrames $Frames -RequireMinimap:$RequireMinimap
}
function New-MinimapProbeFixture([string]$Role = 'Local') {
    $dir = New-ProbeFixture
    Set-ProbeFixtureReport $dir {
        param($report)
        $seat = if ($Role -ceq 'JoinLan') { 1 } else { 0 }
        $fields = @{
            role = $Role; local_seat = $seat; minimap_layout_valid = $true
            active_minimap_count = 1
            minimap_local_seats = @($seat); minimap_player_counts = @(2); minimap_ball_counts = @(8)
            minimap_screen_rects = @([pscustomobject]@{ x = 52; y = 26; width = 10; height = 18 })
        }
        if ($Role -ceq 'Local') {
            $fields.active_minimap_count = 2
            $fields.minimap_local_seats = @(0, 1); $fields.minimap_player_counts = @(2, 2); $fields.minimap_ball_counts = @(8, 8)
            $fields.minimap_screen_rects = @(
                [pscustomobject]@{ x = 20; y = 26; width = 10; height = 18 },
                [pscustomobject]@{ x = 52; y = 26; width = 10; height = 18 })
        }
        foreach ($name in $fields.Keys) { $report | Add-Member -NotePropertyName $name -NotePropertyValue $fields[$name] -Force }
    }
    return $dir
}
function Replace-FixtureImage([string]$Directory, [string]$Kind) {
    $width = if ($Kind -eq 'wrong-size') { 63 } else { 64 }
    $bitmap = New-Object Drawing.Bitmap $width, 48
    try {
        for ($y = 0; $y -lt 48; $y++) {
            for ($x = 0; $x -lt $width; $x++) {
                $color = [Drawing.Color]::Black
                if ($Kind -eq 'solid') { $color = [Drawing.Color]::White }
                if ($Kind -eq 'near-black') { $color = [Drawing.Color]::FromArgb(255, $x % 8, $y % 8, 0) }
                if ($Kind -eq 'sparse' -and $y -eq 0 -and $x -lt 8) { $color = [Drawing.Color]::FromArgb(255, 50 + $x * 20, 100, 100) }
                $bitmap.SetPixel($x, $y, $color)
            }
        }
        $bitmap.Save((Join-Path $Directory 'player-after-sampling.png'), [Drawing.Imaging.ImageFormat]::Png)
    }
    finally { $bitmap.Dispose() }
}
try {
    foreach ($frames in @(60, 600)) {
        $dir = New-ProbeFixture $frames
        $validated = Assert-ProbeFixture $dir $frames
        if ($validated.frames -ne $frames -or $validated.png_checks[0].sampled_rgb_colours -lt 8) { throw 'Valid synthetic probe was not fully checked.' }
        $script:checks++
    }
    foreach ($kind in @('black', 'solid', 'near-black', 'sparse', 'wrong-size')) {
        $dir = New-ProbeFixture
        Replace-FixtureImage $dir $kind
        $message = if ($kind -eq 'wrong-size') { 'dimensions differ' } elseif ($kind -in @('near-black', 'sparse')) { 'less than 1%' } else { 'fewer than 8' }
        Assert-ProbeRejected { Assert-ProbeFixture $dir } $kind $message
    }
    $dir = New-ProbeFixture
    [IO.File]::WriteAllText((Join-Path $dir 'player-after-sampling.png'), 'not a PNG')
    Assert-ProbeRejected { Assert-ProbeFixture $dir } 'corrupt PNG' 'could not be decoded'
    $dir = New-ProbeFixture
    Set-ProbeFixtureReport $dir { param($report) $report.screenshots = @(Join-Path $dir 'missing.png') }
    Assert-ProbeRejected { Assert-ProbeFixture $dir } 'missing PNG' 'Missing presentation PNG'
    $dir = New-ProbeFixture
    Move-Item -LiteralPath (Join-Path $dir 'frame-intervals.csv') -Destination (Join-Path $dir 'retained-csv.txt')
    Assert-ProbeRejected { Assert-ProbeFixture $dir } 'missing CSV' 'Missing presentation CSV'

    foreach ($kind in @('header', 'short', 'extra', 'duplicate', 'fractional-index', 'nan', 'infinity', 'zero', 'negative', 'empty', 'extra-column', 'nonnumeric')) {
        $dir = New-ProbeFixture
        $path = Join-Path $dir 'frame-intervals.csv'
        $lines = [IO.File]::ReadAllLines($path)
        switch ($kind) {
            'header' { $lines[0] = 'index,milliseconds' }
            'short' { $lines = $lines[0..599] }
            'extra' { $lines += '600,10.000000' }
            'duplicate' { $lines[2] = '0,4.010000' }
            'fractional-index' { $lines[2] = '1.5,4.010000' }
            'nan' { $lines[2] = '1,NaN' }
            'infinity' { $lines[2] = '1,Infinity' }
            'zero' { $lines[2] = '1,0' }
            'negative' { $lines[2] = '1,-1' }
            'empty' { $lines[2] = '1,' }
            'extra-column' { $lines[2] = '1,4.010000,unexpected' }
            'nonnumeric' { $lines[2] = '1,word' }
        }
        [IO.File]::WriteAllLines($path, $lines)
        Assert-ProbeRejected { Assert-ProbeFixture $dir } ('CSV ' + $kind) 'Presentation CSV'
    }
    foreach ($name in @('p50_frame_ms', 'p95_frame_ms', 'max_frame_ms')) {
        $dir = New-ProbeFixture
        Set-ProbeFixtureReport $dir { param($report) $report.$name += 1 }
        Assert-ProbeRejected { Assert-ProbeFixture $dir } ('wrong ' + $name) 'differs from independently recomputed'
        $dir = New-ProbeFixture
        Set-ProbeFixtureReport $dir { param($report) $report.$name = 'NaN' }
        Assert-ProbeRejected { Assert-ProbeFixture $dir } ('non-finite ' + $name) 'non-finite presentation field'
    }
    foreach ($mutation in @(
        @('complete', $false), @('passed', 'true'), @('editor_run', $true), @('sampled_frames', 599),
        @('requested_frames', 599), @('width', 63), @('height', 47), @('errors', 1),
        @('presentation_errors', 1), @('Chinese_font_has_all_required_glyphs', $false),
        @('effect_capacity_bounds_held', $false), @('realtime_end', 10), @('tick_end', 100)
    )) {
        $dir = New-ProbeFixture
        Set-ProbeFixtureReport $dir { param($report) $report.($mutation[0]) = $mutation[1] }
        Assert-ProbeRejected { Assert-ProbeFixture $dir } ('report ' + $mutation[0]) ''
    }
    $dir = New-ProbeFixture
    Set-ProbeFixtureReport $dir { param($report) $report.PSObject.Properties.Remove('p95_frame_ms') }
    Assert-ProbeRejected { Assert-ProbeFixture $dir } 'missing summary field' 'non-finite presentation field'
    $dir = New-ProbeFixture
    Set-ProbeFixtureReport $dir { param($report) $report.screenshots = @() }
    Assert-ProbeRejected { Assert-ProbeFixture $dir } 'empty screenshot list' 'no screenshot list'
    $dir = New-ProbeFixture
    Set-ProbeFixtureReport $dir { param($report) $report.screenshots = @('player-after-sampling.png') }
    Assert-ProbeRejected { Assert-ProbeFixture $dir } 'relative screenshot path' 'absolute paths'
    $dir = New-ProbeFixture
    Set-ProbeFixtureReport $dir { param($report) $report.screenshots = @(Join-Path $fixtureRoot 'outside.png') }
    Assert-ProbeRejected { Assert-ProbeFixture $dir } 'unrelated screenshot directory' 'own report directory'
    $dir = New-ProbeFixture
    Copy-Item -LiteralPath (Join-Path $dir 'player-after-sampling.png') -Destination (Join-Path $dir 'unrelated.png')
    Set-ProbeFixtureReport $dir { param($report) $report.screenshots = @(Join-Path $dir 'unrelated.png') }
    Assert-ProbeRejected { Assert-ProbeFixture $dir } 'no final screenshot' 'does not list the final'

    # Historical evidence remains valid by default, but must not be mistaken for
    # minimap release acceptance simply because its old Player wrote passed=true.
    $dir = New-ProbeFixture
    $legacy = Assert-ProbeFixture $dir
    if ($legacy.minimap_required -or $null -ne $legacy.minimap_checks) { throw 'Legacy evidence unexpectedly requires minimap fields.' }
    $script:checks++
    Assert-ProbeRejected { Assert-ProbeFixture $dir -RequireMinimap } 'old report lacks minimap fields' 'minimap_layout_valid'
    foreach ($role in @('Local', 'HostLan', 'JoinLan')) {
        $dir = New-MinimapProbeFixture $role
        $checked = Assert-ProbeFixture $dir -RequireMinimap
        $count = if ($role -eq 'Local') { 2 } else { 1 }
        if (!$checked.minimap_required -or !$checked.minimap_checks.layout_fields_validated -or $checked.minimap_checks.map_count -ne $count) {
            throw "Valid $role minimap fixture was not fully checked."
        }
        $script:checks++
    }
    $dir = New-MinimapProbeFixture
    Set-ProbeFixtureReport $dir { param($report) $report.minimap_player_counts = @(0, 0); $report.minimap_ball_counts = @(0, 0) }
    $null = Assert-ProbeFixture $dir -RequireMinimap
    $script:checks++
    foreach ($mutation in @(
        @('false layout', { param($report) $report.minimap_layout_valid = $false }, 'minimap_layout_valid'),
        @('string layout', { param($report) $report.minimap_layout_valid = 'true' }, 'minimap_layout_valid'),
        @('unknown role', { param($report) $report.role = 'Observer' }, 'Minimap role'),
        @('wrong-case role', { param($report) $report.role = 'local' }, 'Minimap role'),
        @('missing role', { param($report) $report.PSObject.Properties.Remove('role') }, 'Minimap role'),
        @('wrong local seat', { param($report) $report.local_seat = 1 }, 'Minimap local_seat'),
        @('fractional local seat', { param($report) $report.local_seat = 0.5 }, 'Minimap local_seat'),
        @('wrong count', { param($report) $report.active_minimap_count = 1 }, 'Minimap count'),
        @('fractional count', { param($report) $report.active_minimap_count = 1.5 }, 'Minimap count'),
        @('duplicate seats', { param($report) $report.minimap_local_seats = @(0, 0) }, 'Minimap seats'),
        @('fractional seat', { param($report) $report.minimap_local_seats = @(0, 0.5) }, 'Minimap seat must'),
        @('seat out of range', { param($report) $report.minimap_local_seats = @(0, 2) }, 'Minimap seat must'),
        @('player counts disagree', { param($report) $report.minimap_player_counts = @(2, 1) }, 'entity counts must agree'),
        @('ball counts disagree', { param($report) $report.minimap_ball_counts = @(8, 7) }, 'entity counts must agree'),
        @('null rectangle', { param($report) $report.minimap_screen_rects[0] = $null }, 'rectangle is missing'),
        @('zero width', { param($report) $report.minimap_screen_rects[0].width = 0 }, 'Minimap rectangle must'),
        @('negative height', { param($report) $report.minimap_screen_rects[0].height = -1 }, 'Minimap rectangle must'),
        @('left of right quadrant', { param($report) $report.minimap_screen_rects[0].x = 10 }, 'Minimap rectangle must'),
        @('bottom of map outside top quadrant', { param($report) $report.minimap_screen_rects[0].y = 20 }, 'Minimap rectangle must'),
        @('right outside seat viewport', { param($report) $report.minimap_screen_rects[0].width = 20 }, 'Minimap rectangle must'),
        @('top outside screen', { param($report) $report.minimap_screen_rects[0].height = 30 }, 'Minimap rectangle must')
    )) {
        $dir = New-MinimapProbeFixture
        Set-ProbeFixtureReport $dir $mutation[1]
        Assert-ProbeRejected { Assert-ProbeFixture $dir -RequireMinimap } $mutation[0] $mutation[2]
    }
    foreach ($role in @('HostLan', 'JoinLan')) {
        $dir = New-MinimapProbeFixture $role
        Set-ProbeFixtureReport $dir { param($report) $report.minimap_local_seats = @(1 - $report.local_seat) }
        Assert-ProbeRejected { Assert-ProbeFixture $dir -RequireMinimap } "$role wrong map seat" 'Minimap seats'
        $dir = New-MinimapProbeFixture $role
        Set-ProbeFixtureReport $dir { param($report) $report.local_seat = 1 - $report.local_seat }
        Assert-ProbeRejected { Assert-ProbeFixture $dir -RequireMinimap } "$role wrong local seat" 'Minimap local_seat'
    }
    foreach ($name in @('minimap_local_seats', 'minimap_player_counts', 'minimap_ball_counts', 'minimap_screen_rects')) {
        foreach ($kind in @('missing', 'short', 'extra', 'null', 'scalar')) {
            $dir = New-MinimapProbeFixture
            Set-ProbeFixtureReport $dir {
                param($report)
                switch ($kind) {
                    'missing' { $report.PSObject.Properties.Remove($name) }
                    'short' { $report.$name = @($report.$name[0]) }
                    'extra' { $report.$name = @($report.$name) + @($report.$name[0]) }
                    'null' { $report.$name = $null }
                    'scalar' { $report.$name = $report.$name[0] }
                }
            }
            Assert-ProbeRejected { Assert-ProbeFixture $dir -RequireMinimap } "$name $kind" 'Minimap array'
        }
    }
    foreach ($field in @('x', 'y', 'width', 'height')) {
        foreach ($value in @('NaN', 'Infinity', 'not-a-number', $null)) {
            $dir = New-MinimapProbeFixture
            Set-ProbeFixtureReport $dir { param($report) $report.minimap_screen_rects[0].$field = $value }
            Assert-ProbeRejected { Assert-ProbeFixture $dir -RequireMinimap } "rectangle $field=$value" 'non-finite presentation field'
        }
    }
    foreach ($field in @(@('minimap_player_counts', 3), @('minimap_ball_counts', 9))) {
        foreach ($value in @(-1, $field[1], 0.5, 'NaN', 'Infinity', $null)) {
            $dir = New-MinimapProbeFixture
            Set-ProbeFixtureReport $dir { param($report) $report.($field[0])[0] = $value }
            Assert-ProbeRejected { Assert-ProbeFixture $dir -RequireMinimap } "$($field[0])=$value" ''
        }
    }
    Write-Output "PASS: $script:checks synthetic presentation integrity checks. No Player, rendering, gameplay or window-control acceptance is claimed."

    if ($ValidateCurrent013Evidence) {
        $accepted = 0; $rejectedBlack = 0
        foreach ($size in @('1280x720', '1920x1080', '1024x768')) {
            $dimensions = $size.Split('x')
            $dir = Join-Path $PSScriptRoot ('DashArenaPresentationResults\20261003-0.13-local-' + $size)
            $null = Assert-DashPlayerPresentationEvidence -Directory $dir -ExpectedWidth $dimensions[0] -ExpectedHeight $dimensions[1]
            $accepted++
        }
        foreach ($case in @(@('20261003-171940-502-00d187', 1280, 720), @('20261003-172049-560-2be20c', 1920, 1080), @('20261003-172158-697-dcae82', 1024, 768))) {
            foreach ($role in @('host', 'join')) {
                $dir = Join-Path $PSScriptRoot ('LanDiagnosticResults\' + $case[0] + '\' + $role + '-presentation')
                if ($case[0] -eq '20261003-171940-502-00d187' -and $role -eq 'join') {
                    Assert-ProbeRejected { Assert-DashPlayerPresentationEvidence -Directory $dir -ExpectedWidth $case[1] -ExpectedHeight $case[2] } 'retained actual 0.13 black screenshot' 'fewer than 8'
                    $rejectedBlack++
                } else {
                    $null = Assert-DashPlayerPresentationEvidence -Directory $dir -ExpectedWidth $case[1] -ExpectedHeight $case[2]
                    $accepted++
                }
            }
        }
        if ($accepted -ne 8 -or $rejectedBlack -ne 1) { throw 'Actual retained-evidence audit did not cover all expected reports.' }
        Write-Output "PASS: read-only audit of retained 0.13 evidence accepted $accepted intact reports and rejected $rejectedBlack black screenshot. Manual layout and performance interpretation remain separate."
    }
}
finally {
    # Only this invocation's explicitly named synthetic temp directory is removed.
    $resolved = [IO.Path]::GetFullPath($fixtureRoot)
    $tempRoot = [IO.Path]::GetFullPath([IO.Path]::GetTempPath()).TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($resolved.StartsWith($tempRoot, [StringComparison]::OrdinalIgnoreCase) -and
        [IO.Path]::GetFileName($resolved) -ceq $fixtureName -and $fixtureName -match '^PocketBump-PresentationEvidence-[0-9a-f]{32}$') {
        Remove-Item -LiteralPath $resolved -Recurse -Force
    } else { throw 'Refusing cleanup of an unexpected synthetic fixture path.' }
}
