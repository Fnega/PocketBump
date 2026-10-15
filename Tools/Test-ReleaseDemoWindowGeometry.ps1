$ErrorActionPreference = 'Stop'
# Pure rectangle fixtures only: do not initialize Win32 APIs, enumerate desktop
# windows, start a Player, reposition a window, or send mouse/keyboard input.
. (Join-Path $PSScriptRoot 'ReleaseDemoWindows.ps1')
$script:geometryChecks = 0

function New-GeometryRect([int]$Left, [int]$Top, [int]$Right, [int]$Bottom) {
    return [pscustomobject]@{ Left=$Left; Top=$Top; Right=$Right; Bottom=$Bottom }
}
function Assert-Geometry([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw "Geometry fixture failed: $Label" }
    $script:geometryChecks++
}
function Assert-GeometryRejected([scriptblock]$Action, [string]$Label) {
    $rejected = $false
    try { $null = & $Action } catch { $rejected = $true }
    Assert-Geometry $rejected $Label
}
function Assert-Placement($Outer, [object[]]$Areas, [int]$X, [int]$Y, [bool]$Move, [string]$Label) {
    $placement = Get-ReleaseDemoWindowPlacement -Outer $Outer -WorkAreas $Areas
    Assert-Geometry ($placement.x -eq $X -and $placement.y -eq $Y -and $placement.move -eq $Move) $Label
    $placed = New-GeometryRect $placement.x $placement.y `
        ($placement.x + $Outer.Right - $Outer.Left) ($placement.y + $Outer.Bottom - $Outer.Top)
    $fits = $false
    foreach ($area in $Areas) { if (Test-ReleaseDemoRectangleContained $placed $area) { $fits=$true } }
    Assert-Geometry $fits ($Label + ' preserves dimensions inside one work area')
}

$work = New-GeometryRect 0 0 2560 1560
$monitor = New-GeometryRect 0 0 2560 1600
Assert-Placement (New-GeometryRect 100 120 1380 840) @($work) 100 120 $false 'already visible window remains untouched'
Assert-Placement (New-GeometryRect 2400 1400 3680 2120) @($work) 1280 840 $true 'right and bottom overflow clamps inward'
Assert-Placement (New-GeometryRect -80 -60 1200 660) @($work) 0 0 $true 'left and top overflow clamps inward'
Assert-Placement (New-GeometryRect 0 0 2560 1560) @($work) 0 0 $false 'exact work-area edges remain valid'
Assert-Placement (New-GeometryRect 0 0 1280 720) @((New-GeometryRect 0 40 2560 1600)) 0 40 $true 'top taskbar is excluded'

$leftWork = New-GeometryRect -1920 0 0 1040
Assert-Placement (New-GeometryRect -1800 30 -520 750) @($work,$leftWork) -1800 30 $false 'negative-origin monitor remains untouched'
Assert-Placement (New-GeometryRect -2000 900 -720 1620) @($leftWork,$work) -1920 320 $true 'nearest fitting negative-origin work area is selected'
Assert-Placement (New-GeometryRect -300 100 980 820) @($leftWork,$work) 0 100 $true 'cross-monitor window moves into one fitting area'
Assert-Placement (New-GeometryRect 900 800 2180 1520) @((New-GeometryRect 0 0 1024 700),$work) 900 800 $false 'smaller first monitor never forces resize'
Assert-Placement (New-GeometryRect 3000 1700 4280 2420) @((New-GeometryRect 0 0 1024 700),$work) 1280 840 $true 'only fitting work area is eligible'

$fullscreen = Get-ReleaseDemoWindowPlacement -Outer $monitor -WorkAreas @($work) -MonitorBounds @($monitor) -Borderless
Assert-Geometry (!$fullscreen.move -and $fullscreen.region -ceq 'fullscreen_monitor') 'exact borderless fullscreen may cover the taskbar inset'
$leftMonitor = New-GeometryRect -1920 -100 0 980
$leftInset = New-GeometryRect -1920 -100 0 940
$fullscreen = Get-ReleaseDemoWindowPlacement -Outer $leftMonitor -WorkAreas @($leftInset) -MonitorBounds @($leftMonitor,$monitor) -Borderless
Assert-Geometry (!$fullscreen.move -and $fullscreen.x -eq -1920 -and $fullscreen.y -eq -100) 'fullscreen exception supports negative monitor origins'

Assert-GeometryRejected { Get-ReleaseDemoWindowPlacement -Outer $monitor -WorkAreas @($work) -MonitorBounds @($monitor) } 'decorated oversized window cannot use fullscreen exception'
Assert-GeometryRejected { Get-ReleaseDemoWindowPlacement -Outer (New-GeometryRect -1 0 2559 1600) -WorkAreas @($work) -MonitorBounds @($monitor) -Borderless } 'partially off-screen borderless window is not fullscreen'
Assert-GeometryRejected { Get-ReleaseDemoWindowPlacement -Outer (New-GeometryRect 0 0 2800 720) -WorkAreas @($leftWork,$work) } 'combined monitor width cannot justify a window wider than every work area'
Assert-GeometryRejected { Get-ReleaseDemoWindowPlacement -Outer (New-GeometryRect 0 0 100 1700) -WorkAreas @($work) } 'window taller than all work areas is rejected'
Assert-GeometryRejected { Get-ReleaseDemoWindowPlacement -Outer (New-GeometryRect 0 0 0 720) -WorkAreas @($work) } 'zero dimensions are rejected'
Assert-GeometryRejected { Get-ReleaseDemoWindowPlacement -Outer (New-GeometryRect 100 0 0 720) -WorkAreas @($work) } 'negative dimensions are rejected'
Assert-GeometryRejected { Get-ReleaseDemoWindowPlacement -Outer (New-GeometryRect 0 0 1280 720) -WorkAreas @() } 'missing work areas are rejected'

Assert-Geometry (Test-ReleaseDemoRectangleContained (New-GeometryRect 0 0 2560 1560) $work) 'containment accepts exact boundaries'
foreach ($overflow in @((New-GeometryRect -1 0 1280 720), (New-GeometryRect 0 -1 1280 720),
    (New-GeometryRect 0 0 2561 720), (New-GeometryRect 0 0 1280 1561), (New-GeometryRect 0 0 0 720))) {
    Assert-Geometry (!(Test-ReleaseDemoRectangleContained $overflow $work)) 'containment rejects overflow or empty rectangles'
}
Write-Host "Release window pure geometry fixtures passed: $script:geometryChecks. No desktop APIs were invoked."
