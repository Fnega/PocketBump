param(
    [Parameter(Mandatory = $true)][string]$PlayerPath,
    [Parameter(Mandatory = $true)][string]$ReportDirectory,
    [ValidateSet('1.0', '1.1', '1.2')][string]$ExpectedVersion = '1.2',
    [string]$ExpectedBuildGuid = '',
    [switch]$ShowTestWindows,
    [ValidateRange(15, 90)][int]$StageTimeoutSeconds = 40
)

$ErrorActionPreference = 'Stop'
if (!$ShowTestWindows) { throw 'This ordinary-UI acceptance requires explicitly authorized visible test windows: -ShowTestWindows.' }
if (!(Test-Path -LiteralPath $PlayerPath -PathType Leaf)) { throw "Player not found: $PlayerPath" }
$PlayerPath = (Resolve-Path -LiteralPath $PlayerPath).Path
$ReportDirectory = [IO.Path]::GetFullPath($ReportDirectory)
. (Join-Path $PSScriptRoot 'PlayerPresentationWindows.ps1')
. (Join-Path $PSScriptRoot 'ReleaseDemoWindows.ps1')
. (Join-Path $PSScriptRoot 'ReleaseCandidateManifest.ps1')
. (Join-Path $PSScriptRoot 'ReleaseReportObserver.ps1')
$candidateDirectory = Split-Path -Parent $PlayerPath
$candidateManifest = Get-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidateDirectory
# Snapshot names before launching either Player. Keep all historical evidence intact.
$reportObserver = New-ReleaseReportObserver -ReportDirectory $ReportDirectory
Initialize-DashPresentationWindowApi
Initialize-ReleaseDemoInputApi
Add-Type -AssemblyName System.Windows.Forms
$stamp = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$resultDirectory = Join-Path $PSScriptRoot "ReleaseDemoResults\$stamp"
$null = New-Item -ItemType Directory -Path $resultDirectory
$tickets = New-Object Collections.Generic.List[object]
$screenshots = New-Object Collections.Generic.List[object]
$captureAttempts = New-Object Collections.Generic.List[object]
$checks = New-Object Collections.Generic.List[object]
$reportCopies = New-Object Collections.Generic.List[string]
$connectionTimings = New-Object Collections.Generic.List[object]
$stage = 'startup'
$passed = $false
$failure = ''
$restoreWindowed = $false
$originalForeground = [PocketBumpRelease.DesktopInput]::GetForegroundWindow()
$originalCursor = New-Object PocketBumpRelease.Point
$null = [PocketBumpRelease.DesktopInput]::GetCursorPos([ref]$originalCursor)
$previousDpi = [PocketBumpDiagnostics.PresentationWindows]::SetThreadDpiAwarenessContext([IntPtr](-4))
if ($previousDpi -eq [IntPtr]::Zero) { throw 'Physical-pixel DPI awareness could not be established; no players launched.' }

function Start-ReleaseDemoPlayer([string]$Label) {
    $log = Join-Path $resultDirectory ($Label + '-player.log')
    # Only documented Unity window/log arguments. No development/automation hooks.
    $arguments = '-screen-fullscreen 0 -screen-width 1280 -screen-height 720 -logFile "' + $log + '"'
    $process = Start-Process -FilePath $PlayerPath -WorkingDirectory (Split-Path -Parent $PlayerPath) -ArgumentList $arguments -WindowStyle Normal -PassThru
    # Retain this launch's kernel handle before registration. Registration failure
    # must not bypass cleanup merely because the normal ticket list is still empty.
    try {
        $null = $process.Handle
        $ticket = New-DashPresentationWindowTicket -Process $process -ExpectedPlayerPath $PlayerPath
    }
    catch {
        $registrationFailure = $_.Exception.Message
        $cleanupFailure = ''
        try {
            $process.Refresh()
            if (!$process.HasExited) {
                # Reconfirm the same object/start time/exact executable before any
                # close. Missing paths get only the normal bounded startup retry.
                $cleanupTicket = New-DashPresentationWindowTicket -Process $process -ExpectedPlayerPath $PlayerPath
                Assert-DashPresentationWindowIdentity $cleanupTicket
                $null = $process.CloseMainWindow()
                if (!$process.WaitForExit(2000)) {
                    Assert-DashPresentationWindowIdentity $cleanupTicket
                    $process.Kill()
                    if (!$process.WaitForExit(3000)) { throw 'The invocation-owned startup process did not exit after cleanup.' }
                }
            }
        }
        catch { $cleanupFailure = " Startup cleanup refused or failed for owned_pid=$($process.Id): $($_.Exception.Message)" }
        throw ("Could not register invocation-owned Player pid=$($process.Id): $registrationFailure" + $cleanupFailure)
    }
    $ticket | Add-Member label $Label
    $ticket | Add-Member log_path $log
    $tickets.Add($ticket)
    $deadline = [DateTime]::UtcNow.AddSeconds($StageTimeoutSeconds)
    do {
        $window = Get-DashPresentationOwnedWindow $ticket
        if ($null -ne $window -and $window.Visible -and $window.Client.Right -eq 1280 -and $window.Client.Bottom -eq 720) {
            $ticket.window_handle = $window.Handle
            Start-Sleep -Seconds 3
            return $ticket
        }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw 'The owned Player did not reach its visible 1280x720 window. Check saved display settings and its player log.'
}

function Save-ReleaseScreenshot($Ticket, [string]$Name) {
    $path = Join-Path $resultDirectory ($Name + '.png')
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $attemptPath = Join-Path $resultDirectory ($Name + '-capture-attempt-' + $attempt + '.png')
        $reason = ''; $pixels = 0
        try {
            $null = Focus-ReleaseDemoWindow $Ticket
            Start-Sleep -Milliseconds (250 + $attempt * 250)
            $window = Get-DashPresentationOwnedWindow $Ticket
            $monitor = [Windows.Forms.Screen]::FromHandle($window.Handle).Bounds
            $origin = [PocketBumpRelease.DesktopInput]::ClientOrigin($window.Handle)
            $width = $window.Client.Right - $window.Client.Left
            $height = $window.Client.Bottom - $window.Client.Top
            if (!$window.Visible -or $window.Minimized -or $origin.X -lt $monitor.Left -or
                $origin.Y -lt $monitor.Top -or $origin.X + $width -gt $monitor.Right -or $origin.Y + $height -gt $monitor.Bottom) {
                throw 'The complete owned client must fit on its monitor before screenshot capture.'
            }
            if ([PocketBumpRelease.DesktopInput]::GetForegroundWindow() -ne $window.Handle) {
                throw 'Foreground changed before capture; no screenshot taken.'
            }
            $pixels = Save-ReleaseDemoClientPixels $window $attemptPath
            if ([PocketBumpRelease.DesktopInput]::GetForegroundWindow() -ne $window.Handle) {
                # Discard this invocation-created frame because it may show another app.
                Remove-Item -LiteralPath $attemptPath -ErrorAction Stop
                throw 'Foreground changed during capture; untrusted frame discarded.'
            }
            $window = Get-DashPresentationOwnedWindow $Ticket
            if (!$window.Visible -or $window.Minimized) {
                Remove-Item -LiteralPath $attemptPath -ErrorAction Stop
                throw 'Owned window became hidden during capture; untrusted frame discarded.'
            }
            if ($pixels -lt 50) { throw 'Screenshot is empty/black; failed frame retained.' }
            Move-Item -LiteralPath $attemptPath -Destination $path -ErrorAction Stop
            $captureAttempts.Add([pscustomobject]@{ name=$Name; attempt=$attempt; passed=$true; path=$path; reason='' })
            $screenshots.Add([pscustomobject]@{
                name = $Name; path = $path; sha256 = (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
                process_id = $Ticket.process_id; window_handle = $window.Handle.ToInt64()
                width = $window.Client.Right; height = $window.Client.Bottom
                content_review = 'Requires human/image review; non-black does not prove layout correctness.'
            })
            return
        }
        catch { $reason = $_.Exception.Message }
        $retained = if (Test-Path -LiteralPath $attemptPath -PathType Leaf) { $attemptPath } else { '' }
        $captureAttempts.Add([pscustomobject]@{name=$Name; attempt=$attempt; passed=$false; path=$retained; reason=$reason})
        if ($attempt -lt 3) { Start-Sleep -Milliseconds 400 }
    }
    throw "Screenshot could not be verified after three bounded attempts: $Name. $reason"
}

function Get-ReleaseReport($Ticket) {
    return Get-ObservedReleaseReport -Observer $reportObserver -Ticket $Ticket
}

function Wait-ReleaseReport($Ticket, [scriptblock]$Predicate, [string]$Description) {
    $deadline = [DateTime]::UtcNow.AddSeconds($StageTimeoutSeconds)
    do {
        Assert-DashPresentationWindowIdentity $Ticket
        $candidate = Get-ReleaseReport $Ticket
        if ($null -ne $candidate -and (& $Predicate $candidate.data)) { return $candidate }
        Start-Sleep -Milliseconds 250
    } while ([DateTime]::UtcNow -lt $deadline)
    $detail = if ($null -eq $candidate) { 'No matching process report.' } else {
        # Full bounded timelines remain in the report file, not an enormous shell exception.
        $candidate.data | Select-Object report_id,process_id,application_version,build_guid,role,lifecycle,
            simulation_tick,failure_reason,failure_origin,peer_failure_reason,send_attempts,sent_datagrams,
            received_datagrams,receive_polls,receive_failures,last_receive_error | ConvertTo-Json -Compress
    }
    throw "Timed out waiting for $Description. $detail"
}

function Assert-ReleaseIdentity($Report) {
    if ($Report.schema -ne 'pocketbump_lan_release_v1' -or $Report.application_version -ne $ExpectedVersion -or
        $Report.development_build -ne $false -or [string]::IsNullOrWhiteSpace($Report.build_guid)) {
        throw 'Report is not a non-Development player of the expected version with build identity.'
    }
    if ($ExpectedBuildGuid.Length -gt 0 -and $Report.build_guid.Replace('-', '') -ine $ExpectedBuildGuid.Replace('-', '')) {
        throw 'Report build GUID differs from the intended candidate.'
    }
    if ($ExpectedVersion -in @('1.1', '1.2')) {
        if ($Report.protocol_version -ne 8 -or $Report.evidence_version -ne 2 -or
            $Report.timeline_schema -ne 'pocketbump_network_timeline_v1' -or
            $Report.timeline_sample_interval_seconds -ne .25 -or $Report.timeline_retention_seconds -ne 20 -or
            @($Report.timeline).Count -lt 1 -or @($Report.timeline).Count -gt 81 -or
            $Report.receive_failures -ne 0 -or $Report.receive_polls -lt 1 -or $Report.pump_count -lt 1) {
            throw "Release $ExpectedVersion lacks bounded production receive/timeline evidence or reports a receive failure."
        }
        $lastElapsed = -1.0
        foreach ($sample in $Report.timeline) {
            if ($sample.elapsed_seconds -lt $lastElapsed) { throw 'Release timeline observations are not ordered.' }
            $lastElapsed = $sample.elapsed_seconds
        }
        if ($Report.failure_reason.Length -gt 0 -and $Report.timeline_frozen -ne $true) {
            throw 'Release terminal evidence did not freeze its pre-failure timeline.'
        }
        if ($Report.failure_reason -eq 'PEER LEFT' -and
            ($Report.failure_origin -ne 'peer' -or $Report.peer_failure_reason -ne '')) {
            throw 'Voluntary departure was incorrectly classified as a peer fault.'
        }
    }
}

function Save-ReleaseReportCopy($Observation, [string]$Name) {
    Assert-ReleaseIdentity $Observation.data
    $path = Join-Path $resultDirectory ($Name + '.json')
    $Observation.data | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $path -Encoding UTF8
    $reportCopies.Add($path)
}

function Assert-ReleasePair($First, $Second, [string]$Label) {
    foreach ($item in @($First, $Second)) {
        Assert-ReleaseIdentity $item.data
        if ($item.data.failure_reason -ne '' -or $item.data.ready -ne $true -or $item.data.lifecycle -ne 'Playing' -or
            $item.data.simulation_tick -lt 240 -or $item.data.confirmed_state_tick -lt 220 -or $item.data.validated_checksums -lt 1 -or
            $item.data.sent_datagrams -lt 1 -or $item.data.received_datagrams -lt 1 -or
            $item.data.accepted_input_datagrams -lt 1 -or $item.data.send_failures -ne 0 -or $item.data.presentation_error_count -ne 0) {
            throw 'The ordinary Release LAN session lacks healthy confirmed progression.'
        }
    }
    if ($First.data.role -ne 'host' -or $Second.data.role -ne 'join' -or $First.data.local_seat -ne 0 -or $Second.data.local_seat -ne 1 -or
        $First.data.match_id -eq 0 -or $First.data.match_id -ne $Second.data.match_id -or
        $First.data.build_guid -ne $Second.data.build_guid -or $First.data.rules_hash -ne $Second.data.rules_hash -or
        $First.data.layout_hash -ne $Second.data.layout_hash) { throw 'Release peer identity/rules/seat evidence does not agree.' }
    $checks.Add([pscustomobject]@{ name = $Label; passed = $true; scope = 'same_machine_two_process';
        match_id = $First.data.match_id; host_tick = $First.data.simulation_tick; join_tick = $Second.data.simulation_tick;
        host_validated_checksums = $First.data.validated_checksums; join_validated_checksums = $Second.data.validated_checksums;
        host_session_started_utc = $First.data.session_started_utc; join_session_started_utc = $Second.data.session_started_utc;
        join_start_gap_seconds = ($Second.started - $First.started).TotalSeconds })
}

function Start-HostUi($Ticket) {
    Click-ReleaseDemoUi $Ticket .5 .36 0 52
    Click-ReleaseDemoUi $Ticket .5 .42 140.4 -302.4
}
function Prepare-JoinUi($Ticket) {
    Click-ReleaseDemoUi $Ticket .5 .36 0 -24
    Set-ReleaseDemoAddress $Ticket
}
function Confirm-PreparedJoinUi($Ticket) {
    Click-ReleaseDemoUi $Ticket .5 .42 115.2 -122.4
}
function Start-ReleaseLanPairUi($HostTicket, $JoinTicket, [string]$Label) {
    # Enter the address before starting the game's unchanged handshake deadline.
    # All operations still use the normal foreground/owned-window input guards.
    Prepare-JoinUi $JoinTicket
    $hostUiStarted = [DateTime]::UtcNow
    $timing = [pscustomobject]@{ label=$Label; host_process_id=$HostTicket.process_id;
        join_process_id=$JoinTicket.process_id; host_ui_started_utc=$hostUiStarted.ToString('O');
        host_report_id=''; host_bound_observed_utc=''; join_confirmed_utc='' }
    $connectionTimings.Add($timing)
    Start-HostUi $HostTicket
    $bound = Wait-ReleaseReport $HostTicket { param($r)
        $r.role -eq 'host' -and $r.socket_bound -eq $true -and $r.failure_reason -eq '' -and
        [DateTime]::Parse($r.session_started_utc).ToUniversalTime() -ge $hostUiStarted
    } ($Label + ' fresh host bound socket')
    $timing.host_report_id = $bound.data.report_id
    $timing.host_bound_observed_utc = [DateTime]::UtcNow.ToString('O')
    Confirm-PreparedJoinUi $JoinTicket
    $timing.join_confirmed_utc = [DateTime]::UtcNow.ToString('O')
}
function Leave-MatchUi($Ticket) {
    Send-ReleaseDemoKey $Ticket 0x1B
    Click-ReleaseDemoUi $Ticket .5 .2484 0 0 1600 1000
}
function Leave-FaultUi($Ticket) {
    Click-ReleaseDemoUi $Ticket 1 0 -250.5 111.2 1600 1000
}

function Wait-ReleaseClientSize($Ticket, [int]$Width, [int]$Height) {
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    do {
        $window = Get-DashPresentationOwnedWindow $Ticket
        if ($null -ne $window -and $window.Client.Right -eq $Width -and $window.Client.Bottom -eq $Height) { return }
        Start-Sleep -Milliseconds 150
    } while ([DateTime]::UtcNow -lt $deadline)
    throw "Owned Player did not reach expected client dimensions ${Width}x${Height}."
}

try {
    $firstTicket = Start-ReleaseDemoPlayer 'first'
    $stage = 'main-menu-and-guide'
    Write-Host "Release acceptance: $stage"
    Save-ReleaseScreenshot $firstTicket '01-main-menu'
    Click-ReleaseDemoUi $firstTicket 1 0 -190 65
    Save-ReleaseScreenshot $firstTicket '02-chinese-guide'
    Click-ReleaseDemoUi $firstTicket .5 .1173

    $stage = 'display-fullscreen-and-windowed'
    Write-Host "Release acceptance: $stage"
    $monitor = [Windows.Forms.Screen]::FromHandle($firstTicket.window_handle).Bounds
    Click-ReleaseDemoUi $firstTicket .5 .36 0 -144
    Save-ReleaseScreenshot $firstTicket '02a-display-settings'
    $restoreWindowed = $true
    Click-ReleaseDemoUi $firstTicket .5 .42 0 -16.2
    Wait-ReleaseClientSize $firstTicket $monitor.Width $monitor.Height
    Save-ReleaseScreenshot $firstTicket '02b-fullscreen'
    Click-ReleaseDemoUi $firstTicket .5 .36 0 -144
    Click-ReleaseDemoUi $firstTicket .5 .42 0 59.4
    Wait-ReleaseClientSize $firstTicket 1280 720
    $restoreWindowed = $false
    Save-ReleaseScreenshot $firstTicket '02c-windowed-restored'
    $checks.Add([pscustomobject]@{ name = 'ordinary settings fullscreen and restored windowed'; passed = $true;
        fullscreen_width = $monitor.Width; fullscreen_height = $monitor.Height; restored_width = 1280; restored_height = 720 })

    $stage = 'local-play-pause-resume'
    Write-Host "Release acceptance: $stage"
    Click-ReleaseDemoUi $firstTicket .5 .36 0 208
    Save-ReleaseScreenshot $firstTicket '03-local-rules'
    Click-ReleaseDemoUi $firstTicket .5 .42 140.4 -302.4
    Start-Sleep -Seconds 4
    if ($ExpectedVersion -eq '1.2') {
        # Observe the current P2 mapping without changing saved bindings or NumLock.
        # Fresh-default behavior is independently covered by isolated input tests.
        Send-ReleaseDemoKey $firstTicket 0x62
        Save-ReleaseScreenshot $firstTicket '03a-local-keypad2-dash'
    }
    Send-ReleaseDemoKey $firstTicket 0x53 1000
    Save-ReleaseScreenshot $firstTicket '03b-local-ball-closeup'
    Send-ReleaseDemoKey $firstTicket 0x57 800
    Send-ReleaseDemoKey $firstTicket 0x4B
    Save-ReleaseScreenshot $firstTicket '04-local-playing'
    Send-ReleaseDemoKey $firstTicket 0x1B
    Save-ReleaseScreenshot $firstTicket '05-local-paused'
    Send-ReleaseDemoKey $firstTicket 0x44 700
    Save-ReleaseScreenshot $firstTicket '06-local-paused-after-input'
    Send-ReleaseDemoKey $firstTicket 0x1B
    Send-ReleaseDemoKey $firstTicket 0x44 400
    Save-ReleaseScreenshot $firstTicket '07-local-resumed'
    Leave-MatchUi $firstTicket
    Save-ReleaseScreenshot $firstTicket '08-local-return-menu'
    $checks.Add([pscustomobject]@{ name = 'local UI inputs and screenshots collected'; passed = $true;
        limitation = 'Visual review required for local pause/resume; no internal simulation probe used.' })

    $stage = 'first-lan-session'
    Write-Host "Release acceptance: $stage"
    $secondTicket = Start-ReleaseDemoPlayer 'second'
    Start-ReleaseLanPairUi $firstTicket $secondTicket 'first connection'
    $firstHost = Wait-ReleaseReport $firstTicket { param($r) $r.role -eq 'host' -and $r.ready -and $r.simulation_tick -ge 240 } 'host progression'
    $firstJoin = Wait-ReleaseReport $secondTicket { param($r) $r.role -eq 'join' -and $r.ready -and $r.simulation_tick -ge 240 } 'join progression'
    Assert-ReleasePair $firstHost $firstJoin 'first ordinary LAN connection'
    Save-ReleaseReportCopy $firstHost '09-first-host-playing'
    Save-ReleaseReportCopy $firstJoin '10-first-join-playing'
    Save-ReleaseScreenshot $firstTicket '09-first-host-playing'
    Save-ReleaseScreenshot $secondTicket '10-first-join-playing'

    $stage = 'lan-menu-keeps-network-running'
    Write-Host "Release acceptance: $stage"
    $beforeMenu = (Get-ReleaseReport $firstTicket).data.simulation_tick
    Send-ReleaseDemoKey $firstTicket 0x1B
    Save-ReleaseScreenshot $firstTicket '11-host-personal-menu'
    $continued = Wait-ReleaseReport $firstTicket { param($r) $r.ready -and $r.failure_reason -eq '' -and $r.simulation_tick -ge ($beforeMenu + 180) } 'LAN continues while menu open'
    Save-ReleaseReportCopy $continued '12-host-menu-network-progress'
    $checks.Add([pscustomobject]@{ name = 'LAN menu does not stop transport/simulation'; passed = $true;
        before_tick = $beforeMenu; after_tick = $continued.data.simulation_tick })
    Send-ReleaseDemoKey $firstTicket 0x1B

    $stage = 'host-leave-and-peer-visible-failure'
    Write-Host "Release acceptance: $stage"
    Leave-MatchUi $firstTicket
    $hostLeave = Wait-ReleaseReport $firstTicket { param($r) $r.role -eq 'host' -and $r.event_name -eq 'leave' } 'host normal leave'
    $joinFailure = Wait-ReleaseReport $secondTicket { param($r) $r.failure_reason -eq 'PEER LEFT' } 'join peer departure'
    Save-ReleaseReportCopy $hostLeave '13-host-leave'
    Save-ReleaseReportCopy $joinFailure '14-join-peer-left'
    Save-ReleaseScreenshot $secondTicket '14-join-peer-left'
    Leave-FaultUi $secondTicket

    $stage = 'reverse-roles-rejoin'
    Write-Host "Release acceptance: $stage"
    Start-ReleaseLanPairUi $secondTicket $firstTicket 'reverse connection'
    $reverseHost = Wait-ReleaseReport $secondTicket { param($r) $r.role -eq 'host' -and $r.ready -and $r.simulation_tick -ge 240 } 'reverse host progression'
    $reverseJoin = Wait-ReleaseReport $firstTicket { param($r) $r.role -eq 'join' -and $r.ready -and $r.simulation_tick -ge 240 } 'reverse join progression'
    Assert-ReleasePair $reverseHost $reverseJoin 'reverse roles ordinary LAN connection'
    if ($firstHost.data.report_id -eq $reverseJoin.data.report_id -or $firstJoin.data.report_id -eq $reverseHost.data.report_id) {
        throw 'Fresh connection reused an earlier report identity.'
    }
    Save-ReleaseReportCopy $reverseHost '15-reverse-host-playing'
    Save-ReleaseReportCopy $reverseJoin '16-reverse-join-playing'
    Save-ReleaseScreenshot $secondTicket '15-reverse-host-playing'
    Save-ReleaseScreenshot $firstTicket '16-reverse-join-playing'

    $stage = 'join-leave-and-peer-visible-failure'
    Write-Host "Release acceptance: $stage"
    Leave-MatchUi $firstTicket
    $joinLeave = Wait-ReleaseReport $firstTicket { param($r) $r.role -eq 'join' -and $r.event_name -eq 'leave' } 'join normal leave'
    $hostFailure = Wait-ReleaseReport $secondTicket { param($r) $r.role -eq 'host' -and $r.failure_reason -eq 'PEER LEFT' } 'host peer departure'
    Save-ReleaseReportCopy $joinLeave '17-join-leave'
    Save-ReleaseReportCopy $hostFailure '18-host-peer-left'
    Save-ReleaseScreenshot $secondTicket '18-host-peer-left'
    Leave-FaultUi $secondTicket
    $checks.Add([pscustomobject]@{ name = 'both roles leave and opposite peer reports PEER LEFT'; passed = $true })
    $passed = $true
}
catch {
    $failure = $_.Exception.Message
    foreach ($ticket in $tickets) {
        try { Save-ReleaseScreenshot $ticket ('failure-' + $ticket.label) } catch { }
    }
}
finally {
    if ($restoreWindowed -and $null -ne $firstTicket) {
        try {
            Click-ReleaseDemoUi $firstTicket .5 .36 0 -144
            Click-ReleaseDemoUi $firstTicket .5 .42 0 59.4
            Wait-ReleaseClientSize $firstTicket 1280 720
        }
        catch { $failure += ' Windowed restoration could not be confirmed: ' + $_.Exception.Message }
    }
    foreach ($ticket in $tickets) {
        try {
            $ticket.process.Refresh()
            if (!$ticket.process.HasExited) {
                Assert-DashPresentationWindowIdentity $ticket
                $null = $ticket.process.CloseMainWindow()
                if (!$ticket.process.WaitForExit(5000)) {
                    Assert-DashPresentationWindowIdentity $ticket
                    Stop-Process -Id $ticket.process_id -ErrorAction Stop
                    $null = $ticket.process.WaitForExit(3000)
                }
            }
        }
        catch { $passed = $false; $failure += ' Cleanup: ' + $_.Exception.Message }
    }
    $null = [PocketBumpRelease.DesktopInput]::SetCursorPos($originalCursor.X, $originalCursor.Y)
    if ($originalForeground -ne [IntPtr]::Zero) { $null = [PocketBumpRelease.DesktopInput]::SetForegroundWindow($originalForeground) }
    $null = [PocketBumpDiagnostics.PresentationWindows]::SetThreadDpiAwarenessContext($previousDpi)
    $logIssues = @()
    foreach ($ticket in $tickets) {
        if (!(Test-Path -LiteralPath $ticket.log_path -PathType Leaf)) { $logIssues += "Missing Player log: $($ticket.label)"; continue }
        $matchesFound = @(Select-String -LiteralPath $ticket.log_path -Pattern '^.*(?:Exception:|Assertion failed|Crash!!!|Could not allocate memory)' -CaseSensitive:$false)
        foreach ($match in $matchesFound) { $logIssues += "$($ticket.label):$($match.LineNumber): $($match.Line)" }
    }
    if ($logIssues.Count -gt 0) { $passed = $false; $failure += ' Player log errors found.' }
    try {
        $null = Assert-PocketBumpReleaseCandidateManifest -CandidateDirectory $candidateDirectory `
            -ExpectedFiles @($candidateManifest.files) -ExpectedManifestSha256 $candidateManifest.manifest_sha256
    }
    catch { $passed = $false; $failure += ' Candidate payload changed during acceptance: ' + $_.Exception.Message }
    $runtimeAssembly = Join-Path $candidateDirectory 'PocketBump_Data\Managed\PocketBump.Runtime.dll'
    $buildMetadata = Join-Path $candidateDirectory 'build-info.json'
    $summary = [pscustomobject]@{
        schema = 'pocketbump_release_ui_acceptance_v1'; passed = $passed; completed_utc = [DateTime]::UtcNow.ToString('O')
        stage = $stage; failure = $failure; player = $PlayerPath
        player_sha256 = (Get-FileHash -LiteralPath $PlayerPath -Algorithm SHA256).Hash
        runtime_assembly = $runtimeAssembly
        runtime_assembly_sha256 = if (Test-Path -LiteralPath $runtimeAssembly -PathType Leaf) { (Get-FileHash -LiteralPath $runtimeAssembly -Algorithm SHA256).Hash } else { $null }
        build_info = $buildMetadata
        build_info_sha256 = if (Test-Path -LiteralPath $buildMetadata -PathType Leaf) { (Get-FileHash -LiteralPath $buildMetadata -Algorithm SHA256).Hash } else { $null }
        candidate_manifest_schema = $candidateManifest.schema
        candidate_manifest_sha256 = $candidateManifest.manifest_sha256
        candidate_files = @($candidateManifest.files)
        scope = 'same_machine_two_process_ordinary_ui'; two_computer_validated = $false
        local_visual_review_pending = $true; audio_listening_review_pending = $true
        expected_version = $ExpectedVersion; expected_build_guid = $ExpectedBuildGuid
        checks = @($checks.ToArray()); screenshots = @($screenshots.ToArray()); capture_attempts = @($captureAttempts.ToArray())
        reports = @($reportCopies.ToArray()); player_log_issues = $logIssues
        report_observer = Get-ReleaseReportObserverStats -Observer $reportObserver
        connection_timings = @($connectionTimings.ToArray())
        processes = @($tickets | ForEach-Object { [pscustomobject]@{ label = $_.label; process_id = $_.process_id;
            start_time_utc_ticks = $_.start_time_utc_ticks; executable = $_.executable; log = $_.log_path } })
    }
    $summary | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $resultDirectory 'acceptance.json') -Encoding UTF8
}
Write-Output "Release ordinary-UI evidence: $resultDirectory"
if (!$passed) { throw "Release UI acceptance failed at $stage : $failure" }
