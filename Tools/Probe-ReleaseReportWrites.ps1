param([Parameter(Mandatory=$true)][string]$PlayerPath, [switch]$ShowTestWindows)
$ErrorActionPreference='Stop'
if (!$ShowTestWindows) { throw 'Explicit visible-window authorization is required.' }
$PlayerPath=(Resolve-Path -LiteralPath $PlayerPath).Path
. (Join-Path $PSScriptRoot 'PlayerPresentationWindows.ps1')
. (Join-Path $PSScriptRoot 'ReleaseDemoWindows.ps1')
Initialize-DashPresentationWindowApi
Initialize-ReleaseDemoInputApi
$result=Join-Path $PSScriptRoot ('ReleaseReportProbeResults\'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff'))
$null=New-Item -ItemType Directory -Path $result
$ticket=$null
$previousDpi=[PocketBumpDiagnostics.PresentationWindows]::SetThreadDpiAwarenessContext([IntPtr](-4))
try {
    $log=Join-Path $result 'player.log'
    $process=Start-Process -FilePath $PlayerPath -ArgumentList ('-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "'+$log+'"') -WindowStyle Normal -PassThru
    $ticket=New-DashPresentationWindowTicket -Process $process -ExpectedPlayerPath $PlayerPath
    $deadline=[DateTime]::UtcNow.AddSeconds(25)
    do {
        $window=Get-DashPresentationOwnedWindow $ticket
        if ($null -ne $window -and $window.Visible -and $window.Client.Right -eq 1280 -and $window.Client.Bottom -eq 720) { break }
        Start-Sleep -Milliseconds 100
    } while ([DateTime]::UtcNow -lt $deadline)
    if ($null -eq $window) { throw 'Player HWND unavailable.' }
    $ticket.window_handle=$window.Handle
    Start-Sleep -Seconds 3
    Click-ReleaseDemoUi $ticket .5 .36 0 52
    Click-ReleaseDemoUi $ticket .5 .42 140.4 -302.4
    Start-Sleep -Seconds 7
    Send-ReleaseDemoKey $ticket 0x1B
    $window=Focus-ReleaseDemoWindow $ticket
    $null=Save-ReleaseDemoClientPixels $window (Join-Path $result 'report-status-menu.png')
    [pscustomobject]@{player=$PlayerPath; process_id=$ticket.process_id; start_time_utc_ticks=$ticket.start_time_utc_ticks;
        snapshot='report-status-menu.png'; scope='ordinary Release UI after first periodic overwrite'; log=$log} |
        ConvertTo-Json | Set-Content -LiteralPath (Join-Path $result 'probe.json') -Encoding UTF8
}
finally {
    if ($null -ne $ticket) {
        $ticket.process.Refresh()
        if (!$ticket.process.HasExited) {
            Assert-DashPresentationWindowIdentity $ticket
            $null=$ticket.process.CloseMainWindow()
            if (!$ticket.process.WaitForExit(5000)) {
                Assert-DashPresentationWindowIdentity $ticket
                Stop-Process -Id $ticket.process_id -ErrorAction Stop
            }
        }
    }
    $null=[PocketBumpDiagnostics.PresentationWindows]::SetThreadDpiAwarenessContext($previousDpi)
}
Write-Output "Report probe results: $result"
