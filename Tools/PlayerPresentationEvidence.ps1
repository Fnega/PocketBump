# Read-only evidence integrity checks. These do not launch/stop players, control
# windows, modify reports, or establish visual layout, gameplay, or GPU performance.
# A colourful screenshot is necessary evidence, not proof that its layout is correct.

function Get-DashPresentationNumber($Report, [string]$Name) {
    $property = $Report.PSObject.Properties[$Name]
    $value = 0.0
    if ($null -eq $property -or $null -eq $property.Value -or
        ![double]::TryParse([Convert]::ToString($property.Value, [Globalization.CultureInfo]::InvariantCulture),
            [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$value) -or
        [double]::IsNaN($value) -or [double]::IsInfinity($value)) {
        throw "Missing or non-finite presentation field: $Name."
    }
    return $value
}

function Assert-DashPresentationPng {
    param(
        [Parameter(Mandatory = $true)][string]$ImagePath,
        [Parameter(Mandatory = $true)][ValidateRange(1, 16384)][int]$ExpectedWidth,
        [Parameter(Mandatory = $true)][ValidateRange(1, 16384)][int]$ExpectedHeight
    )
    if (!(Test-Path -LiteralPath $ImagePath -PathType Leaf)) { throw "Missing presentation PNG: $ImagePath" }
    Add-Type -AssemblyName System.Drawing -ErrorAction Stop
    $bitmap = $null
    try {
        try { $bitmap = [Drawing.Bitmap]::FromFile([IO.Path]::GetFullPath($ImagePath)) }
        catch { throw "Presentation PNG could not be decoded: $ImagePath. $($_.Exception.Message)" }
        if ($bitmap.RawFormat.Guid -ne [Drawing.Imaging.ImageFormat]::Png.Guid) {
            throw "Presentation image is not encoded as PNG: $ImagePath"
        }
        if ($bitmap.Width -ne $ExpectedWidth -or $bitmap.Height -ne $ExpectedHeight) {
            throw "Presentation PNG dimensions differ: expected ${ExpectedWidth}x${ExpectedHeight}, got $($bitmap.Width)x$($bitmap.Height)."
        }
        # A bounded, uniform grid includes the image edges. RGB diversity ignores
        # alpha; transparent pixels do not count as visible non-black evidence.
        $columns = [Math]::Min(128, $bitmap.Width)
        $rows = [Math]::Min(128, $bitmap.Height)
        $colors = New-Object 'System.Collections.Generic.HashSet[int]'
        $nonBlack = 0
        for ($row = 0; $row -lt $rows; $row++) {
            $y = if ($rows -eq 1) { 0 } else { [int][Math]::Floor($row * ($bitmap.Height - 1.0) / ($rows - 1)) }
            for ($column = 0; $column -lt $columns; $column++) {
                $x = if ($columns -eq 1) { 0 } else { [int][Math]::Floor($column * ($bitmap.Width - 1.0) / ($columns - 1)) }
                $pixel = $bitmap.GetPixel($x, $y)
                [void]$colors.Add(([int]$pixel.R -shl 16) -bor ([int]$pixel.G -shl 8) -bor [int]$pixel.B)
                if ($pixel.A -gt 8 -and [Math]::Max($pixel.R, [Math]::Max($pixel.G, $pixel.B)) -gt 8) { $nonBlack++ }
            }
        }
        $sampleCount = $columns * $rows
        $nonBlackRatio = $nonBlack / [double]$sampleCount
        if ($colors.Count -lt 8) { throw "Presentation PNG has fewer than 8 sampled RGB colours (black/near-solid image): $ImagePath" }
        if ($nonBlackRatio -lt 0.01) { throw "Presentation PNG has less than 1% visible non-near-black samples: $ImagePath" }
        return [pscustomobject]@{
            path = [IO.Path]::GetFullPath($ImagePath)
            width = $bitmap.Width; height = $bitmap.Height
            sampled_pixels = $sampleCount; sampled_rgb_colours = $colors.Count; non_near_black_ratio = $nonBlackRatio
        }
    }
    finally { if ($null -ne $bitmap) { $bitmap.Dispose() } }
}

function Assert-DashMinimapEvidence {
    param([Parameter(Mandatory = $true)]$Report, [int]$Width, [int]$Height)
    if ($Report.minimap_layout_valid -isnot [bool] -or $Report.minimap_layout_valid -ne $true) {
        throw 'Minimap field minimap_layout_valid must be true.'
    }
    if ($Report.role -cnotin @('Local', 'HostLan', 'JoinLan')) { throw 'Minimap role must be Local, HostLan, or JoinLan.' }
    $localSeat = Get-DashPresentationNumber $Report 'local_seat'
    $expectedSeat = if ($Report.role -ceq 'JoinLan') { 1 } else { 0 }
    if ($localSeat -ne $expectedSeat) { throw 'Minimap local_seat differs from the reported role.' }
    $expectedCount = if ($Report.role -ceq 'Local') { 2 } else { 1 }
    if ((Get-DashPresentationNumber $Report 'active_minimap_count') -ne $expectedCount) {
        throw 'Minimap count differs from the reported role.'
    }
    foreach ($name in @('minimap_local_seats', 'minimap_player_counts', 'minimap_ball_counts', 'minimap_screen_rects')) {
        $property = $Report.PSObject.Properties[$name]
        if ($null -eq $property -or $property.Value -isnot [Array] -or $property.Value.Count -ne $expectedCount) {
            throw "Minimap array '$name' must contain exactly $expectedCount entries."
        }
    }
    $seenSeats = New-Object 'System.Collections.Generic.HashSet[int]'
    for ($i = 0; $i -lt $expectedCount; $i++) {
        # Reuse the finite-number parser, then require integer identifiers/counts.
        $entry = [pscustomobject]@{
            seat = $Report.minimap_local_seats[$i]
            players = $Report.minimap_player_counts[$i]
            balls = $Report.minimap_ball_counts[$i]
        }
        foreach ($field in @(@('seat', 1), @('players', 2), @('balls', 8))) {
            $number = Get-DashPresentationNumber $entry $field[0]
            if ($number -lt 0 -or $number -gt $field[1] -or $number -ne [Math]::Floor($number)) {
                throw "Minimap $($field[0]) must be an integer between 0 and $($field[1])."
            }
        }
        $seat = [int]$entry.seat
        if (!$seenSeats.Add($seat) -or ($expectedCount -eq 1 -and $seat -ne $localSeat)) {
            throw 'Minimap seats must be distinct and match the local LAN seat.'
        }
        if ($i -gt 0 -and ($entry.players -ne $Report.minimap_player_counts[0] -or $entry.balls -ne $Report.minimap_ball_counts[0])) {
            throw 'Minimap entity counts must agree between local split-screen maps.'
        }
        $rect = $Report.minimap_screen_rects[$i]
        if ($null -eq $rect) { throw 'Minimap rectangle is missing.' }
        $x = Get-DashPresentationNumber $rect 'x'; $y = Get-DashPresentationNumber $rect 'y'
        $rectWidth = Get-DashPresentationNumber $rect 'width'; $rectHeight = Get-DashPresentationNumber $rect 'height'
        $left = if ($expectedCount -eq 2) { $seat * $Width * 0.5 } else { 0.0 }
        $right = if ($expectedCount -eq 2) { ($seat + 1) * $Width * 0.5 } else { [double]$Width }
        # Screen coordinates start at bottom-left. The whole map, not only its
        # centre, must be in the top-right quadrant of its own viewport. Allow
        # one pixel at the boundaries for Canvas/float rounding only.
        if ($rectWidth -le 0 -or $rectHeight -le 0 -or
            $x -lt ($left + $right) * 0.5 - 1 -or $y -lt $Height * 0.5 - 1 -or
            $x + $rectWidth -gt $right + 1 -or $y + $rectHeight -gt $Height + 1) {
            throw 'Minimap rectangle must be positive, contained, and entirely in its viewport top-right quadrant.'
        }
    }
    return [pscustomobject]@{ role = $Report.role; local_seat = $localSeat; map_count = $expectedCount; layout_fields_validated = $true }
}

function Assert-DashPlayerPresentationEvidence {
    param(
        [Parameter(Mandatory = $true)][string]$Directory,
        [Parameter(Mandatory = $true)][ValidateRange(1, 16384)][int]$ExpectedWidth,
        [Parameter(Mandatory = $true)][ValidateRange(1, 16384)][int]$ExpectedHeight,
        [ValidateRange(2, 18000)][int]$ExpectedFrames = 600,
        [switch]$RequireMinimap
    )
    $root = [IO.Path]::GetFullPath($Directory).TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar)
    $reportPath = Join-Path $root 'player-presentation-probe.json'
    $csvPath = Join-Path $root 'frame-intervals.csv'
    if (!(Test-Path -LiteralPath $reportPath -PathType Leaf)) { throw "Missing presentation JSON: $reportPath" }
    try { $report = Get-Content -LiteralPath $reportPath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop }
    catch { throw "Invalid presentation JSON: $reportPath. $($_.Exception.Message)" }
    foreach ($name in @('complete', 'passed', 'Chinese_font_has_all_required_glyphs', 'effect_capacity_bounds_held')) {
        if ($report.$name -isnot [bool] -or $report.$name -ne $true) { throw "Presentation field '$name' must be true." }
    }
    if ($report.editor_run -isnot [bool] -or $report.editor_run -ne $false) { throw 'Presentation evidence must come from an actual Player, not the Editor.' }
    foreach ($name in @('errors', 'presentation_errors')) {
        if ((Get-DashPresentationNumber $report $name) -ne 0) { throw "Presentation field '$name' must be zero." }
    }
    if ((Get-DashPresentationNumber $report 'width') -ne $ExpectedWidth -or
        (Get-DashPresentationNumber $report 'height') -ne $ExpectedHeight) { throw 'Presentation JSON dimensions differ from the requested dimensions.' }
    foreach ($name in @('requested_frames', 'sampled_frames')) {
        if ((Get-DashPresentationNumber $report $name) -ne $ExpectedFrames) { throw "Presentation field '$name' differs from expected frames." }
    }
    $duration = (Get-DashPresentationNumber $report 'realtime_end') - (Get-DashPresentationNumber $report 'realtime_start')
    $tickDelta = (Get-DashPresentationNumber $report 'tick_end') - (Get-DashPresentationNumber $report 'tick_start')
    if ($duration -le 0 -or $tickDelta -le 0) { throw 'Presentation sampling lacks positive time or simulation progress.' }
    # Optional for historical packages that predate minimaps. New minimap release
    # acceptance must opt in; a generic passed=true report is not sufficient.
    $minimapCheck = $null
    if ($RequireMinimap) { $minimapCheck = Assert-DashMinimapEvidence -Report $report -Width $ExpectedWidth -Height $ExpectedHeight }

    if (!(Test-Path -LiteralPath $csvPath -PathType Leaf)) { throw "Missing presentation CSV: $csvPath" }
    $lines = [IO.File]::ReadAllLines($csvPath)
    if ($lines.Count -ne $ExpectedFrames + 1 -or $lines[0] -cne 'sample,unscaled_frame_ms') {
        throw 'Presentation CSV requires the exact header and expected sample count.'
    }
    $intervals = New-Object 'System.Collections.Generic.List[double]'
    for ($i = 0; $i -lt $ExpectedFrames; $i++) {
        $cells = $lines[$i + 1].Split(',')
        $sampleIndex = 0; $milliseconds = 0.0
        if ($cells.Count -ne 2 -or
            ![int]::TryParse($cells[0], [Globalization.NumberStyles]::Integer, [Globalization.CultureInfo]::InvariantCulture, [ref]$sampleIndex) -or
            $sampleIndex -ne $i -or
            ![double]::TryParse($cells[1], [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$milliseconds) -or
            [double]::IsNaN($milliseconds) -or [double]::IsInfinity($milliseconds) -or $milliseconds -le 0) {
            throw "Presentation CSV sample $i must have its consecutive index and a finite positive interval."
        }
        $intervals.Add($milliseconds)
    }
    $sorted = @($intervals | Sort-Object)
    $computed = @{}
    foreach ($percentile in @(@('p50_frame_ms', 0.5), @('p95_frame_ms', 0.95))) {
        $at = ($sorted.Count - 1) * $percentile[1]
        $lower = [int][Math]::Floor($at); $upper = [int][Math]::Ceiling($at)
        $computed[$percentile[0]] = $sorted[$lower] + ($sorted[$upper] - $sorted[$lower]) * ($at - $lower)
    }
    $computed['max_frame_ms'] = $sorted[-1]
    foreach ($name in @('p50_frame_ms', 'p95_frame_ms', 'max_frame_ms')) {
        $reported = Get-DashPresentationNumber $report $name
        # CSV stores six decimals while the Player calculates percentiles as floats.
        $tolerance = [Math]::Max(0.001, [Math]::Abs($computed[$name]) * 0.000002)
        if ([Math]::Abs($reported - $computed[$name]) -gt $tolerance) { throw "Presentation summary '$name' differs from independently recomputed CSV values." }
    }

    $requiredImage = Join-Path $root 'player-after-sampling.png'
    $listedImages = @($report.screenshots)
    if ($listedImages.Count -eq 0) { throw 'Presentation JSON has no screenshot list.' }
    $foundFinal = $false
    $pngChecks = @()
    foreach ($image in $listedImages) {
        if ($image -isnot [string] -or ![IO.Path]::IsPathRooted($image)) { throw 'Presentation screenshots must be absolute paths.' }
        $imagePath = [IO.Path]::GetFullPath($image)
        if (![string]::Equals([IO.Path]::GetDirectoryName($imagePath), $root, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Presentation screenshot must be inside its own report directory.'
        }
        if ([string]::Equals($imagePath, $requiredImage, [StringComparison]::OrdinalIgnoreCase)) { $foundFinal = $true }
        $pngChecks += Assert-DashPresentationPng -ImagePath $imagePath -ExpectedWidth $ExpectedWidth -ExpectedHeight $ExpectedHeight
    }
    if (!$foundFinal) { throw 'Presentation JSON does not list the final player-after-sampling.png screenshot.' }
    return [pscustomobject]@{
        directory = $root; frames = $intervals.Count; duration_seconds = $duration; tick_delta = $tickDelta
        p50_frame_ms = $computed['p50_frame_ms']; p95_frame_ms = $computed['p95_frame_ms']; max_frame_ms = $computed['max_frame_ms']
        png_checks = $pngChecks
        minimap_required = [bool]$RequireMinimap; minimap_checks = $minimapCheck
        scope = 'Evidence integrity only; manual layout review and performance-scope interpretation are still required.'
    }
}
