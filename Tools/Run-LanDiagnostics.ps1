param(
    [Parameter(Mandatory = $true)]
    [string]$PlayerPath,
    [ValidateRange(1, 3600)]
    [int]$TimeoutSeconds = 70,
    [ValidateRange(1, 300)]
    [int]$HostStartupTimeoutSeconds = 30,
    [ValidateRange(300, 36000)]
    [int]$SimulationTicks = 900,
    [switch]$DropFirstControlPacket,
    [switch]$ExpectVersionMismatch,
    [ValidateSet('smoke', 'lifecycle', 'full-match', 'mechanics', 'pacing', 'fault-evidence')]
    [string]$Scenario = 'smoke',
    [ValidateSet('none', 'drop-finish', 'drop-finish-ack', 'drop-consent', 'drop-consent-ack', 'drop-offer',
        'drop-prepared', 'drop-commit', 'drop-commit-ack', 'drop-start', 'drop-startup-input',
        'drop-startup-ack', 'duplicate-reorder', 'stale-epoch', 'peer-silence')]
    [string]$FaultProfile = 'none',
    [ValidateSet('host', 'join')]
    [string]$LeaveRole = 'host',
    [ValidateSet('host', 'join')]
    [string]$PacingStallRole = 'host',
    [ValidateSet('host', 'join')]
    [string]$SilenceRole = 'host',
    [ValidateSet('stun-recovery', 'green-impact', 'purple-immunity', 'blue-teleport', 'blue-no-safe',
        'fall-respawn', 'wall-player-repair', 'wall-ball-repair')]
    [string]$Mechanic = 'stun-recovery',
    [ValidateRange(0, 1)]
    [int]$MechanicSeat = 0,
    [switch]$CapturePresentation,
    [switch]$ShowPresentationWindows,
    [ValidateSet('1280x720', '1920x1080', '1024x768')]
    [string]$ProbeSize = '1280x720',
    [ValidateRange(60, 3600)]
    [int]$ProbeFrames = 600,
    [ValidateRange(30, 3600)]
    [int]$ScenarioTimeoutSeconds = 600
)

$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LanDiagnosticEvidence.ps1')
if ($ShowPresentationWindows -and !$CapturePresentation) { throw 'Visible test windows require -CapturePresentation.' }
if ($CapturePresentation) { . (Join-Path $PSScriptRoot 'PlayerPresentationEvidence.ps1') }
if ($ShowPresentationWindows) { . (Join-Path $PSScriptRoot 'PlayerPresentationWindows.ps1') }
if (!(Test-Path -LiteralPath $PlayerPath -PathType Leaf)) {
    throw "Development player was not found: $PlayerPath"
}
if ($Scenario -in @('smoke', 'mechanics', 'pacing', 'fault-evidence') -and $FaultProfile -ne 'none') {
    throw 'Lifecycle fault profiles require -Scenario lifecycle or full-match.'
}
if ($Scenario -eq 'pacing' -and ($DropFirstControlPacket -or $ExpectVersionMismatch)) {
    throw 'Pacing is a separate bounded simulation-stall scenario; do not combine packet loss or version rejection.'
}
if ($Scenario -eq 'fault-evidence' -and ($DropFirstControlPacket -or $ExpectVersionMismatch)) {
    throw 'Fault evidence is an independent directional result-hold silence scenario.'
}
if ($ExpectVersionMismatch -and ($Scenario -ne 'smoke' -or $DropFirstControlPacket)) {
    throw 'Version mismatch is a separate smoke negative test; do not combine it with handshake-loss injection.'
}
if ($DropFirstControlPacket -and $FaultProfile -ne 'none') {
    throw 'Run handshake loss and lifecycle fault profiles separately so actual injection evidence cannot mask an unexercised profile.'
}
if ($CapturePresentation) {
    if ($Scenario -ne 'smoke' -or $ExpectVersionMismatch) { throw 'Actual-Player presentation capture is a healthy movement-smoke observation.' }
    $SimulationTicks = [Math]::Max(3600, $SimulationTicks)
}
$PlayerPath = (Resolve-Path -LiteralPath $PlayerPath).Path
$expectedDashProtocol = 0
$buildInfoPath = Join-Path (Split-Path -Parent $PlayerPath) 'build-info.json'
if (Test-Path -LiteralPath $buildInfoPath -PathType Leaf) {
    $diagnosticBuildInfo = Get-Content -LiteralPath $buildInfoPath -Raw -Encoding UTF8 | ConvertFrom-Json
    $expectedDashProtocol = [int]$diagnosticBuildInfo.protocol
}
if ($Scenario -eq 'fault-evidence' -and ($expectedDashProtocol -ne 8 -or $diagnosticBuildInfo.development -ne $true)) {
    throw 'Fault evidence requires an identified protocol-8 Development build; never use the Release candidate.'
}
$stamp = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$resultDir = Join-Path $PSScriptRoot "LanDiagnosticResults\$stamp"
New-Item -ItemType Directory -Path $resultDir -Force | Out-Null
$hostResult = Join-Path $resultDir 'host.txt'
$joinResult = Join-Path $resultDir 'join.txt'
$hostLog = Join-Path $resultDir 'host-player.log'
$joinLog = Join-Path $resultDir 'join-player.log'
$hostReady = Join-Path $resultDir 'host.ready'
$summary = Join-Path $resultDir 'launcher-summary.txt'
$launchedProcesses = @()
$launcherStage = 'starting-host'
$launcherFailure = ''
$failureReportGraceSeconds = 20
$failureReportDeadline = $null
$exitStatus = 2
$effectiveTimeoutSeconds = if ($Scenario -eq 'smoke') {
    [Math]::Max($TimeoutSeconds, [Math]::Ceiling($SimulationTicks / 60.0 * 3 + 15))
} else { $ScenarioTimeoutSeconds + 30 }

function Get-PlayerArguments([string]$Role, [string]$ResultPath, [string]$LogPath) {
    $arguments = @(
        "--lan-smoke=$Role"
        "--lan-smoke-ticks=$SimulationTicks"
        ('--lan-smoke-result="' + $ResultPath + '"')
        "--lan-scenario=$Scenario"
        "--lan-fault-profile=$FaultProfile"
        "--lan-leave-role=$LeaveRole"
        "--lan-pacing-stall-role=$PacingStallRole"
        "--lan-silence-role=$SilenceRole"
        "--lan-mechanic=$Mechanic"
        "--lan-mechanic-seat=$MechanicSeat"
        "--lan-scenario-timeout=$ScenarioTimeoutSeconds"
        ('--lan-coordination="' + $resultDir + '"')
        ('-logFile "' + $LogPath + '"')
    )
    if ($Role -eq 'host') { $arguments += ('--lan-smoke-ready="' + $hostReady + '"') }
    if ($DropFirstControlPacket) { $arguments += '--lan-smoke-drop-first' }
    if ($ExpectVersionMismatch) {
        $arguments += '--lan-smoke-expect-mismatch'
        if ($Role -eq 'join') { $arguments += '--lan-smoke-old-version' }
    }
    if ($CapturePresentation) {
        $probeOutput = Join-Path $resultDir ($Role + '-presentation')
        $arguments += @('--dash-probe=observe', ('--dash-probe-output="' + $probeOutput + '"'),
            "--dash-probe-frames=$ProbeFrames", '--dash-probe-warmup=180', "--dash-probe-size=$ProbeSize", '--dash-probe-display=windowed')
    }
    return ($arguments -join ' ')
}

function Get-ProbeReportPath([string]$Role) {
    return Join-Path (Join-Path $resultDir ($Role + '-presentation')) 'player-presentation-probe.json'
}
function Test-ProbeReport([string]$Role) {
    if (!$CapturePresentation) { return $true }
    $path = Get-ProbeReportPath $Role
    if (!(Test-Path -LiteralPath $path -PathType Leaf)) { return $false }
    try { $probe = Get-Content -LiteralPath $path -Raw | ConvertFrom-Json; return $null -ne $probe.passed }
    catch { return $false } # The observer may still be finishing its CreateNew write.
}
function Assert-ProbeReports {
    if (!$CapturePresentation) { return }
    foreach ($role in @('host', 'join')) {
        $probe = Get-Content -LiteralPath (Get-ProbeReportPath $role) -Raw | ConvertFrom-Json
        if ($probe.passed -ne $true -or $probe.complete -ne $true -or $probe.editor_run -ne $false -or
            $probe.sampled_frames -ne $ProbeFrames -or $probe.active_camera_count -ne 1 -or
            $probe.full_screen_mode -ne 'Windowed' -or "$($probe.width)x$($probe.height)" -ne $ProbeSize -or
            $probe.errors -ne 0 -or $probe.presentation_errors -ne 0 -or $probe.screenshots.Count -lt 1) {
            throw "The $role actual-Player probe lacks complete, clean, correctly sized single-screen evidence."
        }
        foreach ($png in $probe.screenshots) {
            if (!(Test-Path -LiteralPath $png -PathType Leaf)) { throw "Missing Player screenshot: $png" }
        }
        $dimensions = $ProbeSize.Split('x')
        $null = Assert-DashPlayerPresentationEvidence -Directory (Split-Path -Parent (Get-ProbeReportPath $role)) `
            -ExpectedWidth ([int]$dimensions[0]) -ExpectedHeight ([int]$dimensions[1]) -ExpectedFrames $ProbeFrames
    }
}

function Test-FinalReport([string]$ReportPath) {
    if (!(Test-Path -LiteralPath $ReportPath -PathType Leaf)) { return $false }
    $reportText = Get-Content -LiteralPath $ReportPath -Raw
    if ($Scenario -eq 'smoke') { return $reportText -match '(?m)^result=(PASS|FAIL)\r?$' }
    return $reportText -match '(?m)^whole_scenario_final=true\r?$'
}

function Read-KeyValueReport([string]$ReportPath) {
    $values = @{}
    foreach ($line in (Get-Content -LiteralPath $ReportPath)) {
        if ($line -match '^([^=]+)=(.*)$') { $values[$matches[1]] = $matches[2] }
    }
    return $values
}

function Assert-PairEvidence {
    $hostValues = Read-KeyValueReport $hostResult
    $joinValues = Read-KeyValueReport $joinResult
    if ($hostValues['report_schema'] -eq 'dash_arena_v1' -or $joinValues['report_schema'] -eq 'dash_arena_v1') {
        Assert-DashPairEvidence $hostValues $joinValues $Scenario $FaultProfile $LeaveRole ([bool]$ExpectVersionMismatch) `
            ([bool]$DropFirstControlPacket) $Mechanic $MechanicSeat $PacingStallRole $SilenceRole $expectedDashProtocol
        if ($Scenario -eq 'fault-evidence') {
            Assert-DashFaultReportFiles $hostValues $hostResult $diagnosticBuildInfo.build_guid
            Assert-DashFaultReportFiles $joinValues $joinResult $diagnosticBuildInfo.build_guid
        }
        return
    }
    if ($ExpectVersionMismatch -or $Scenario -in @('mechanics', 'pacing', 'fault-evidence')) { throw 'This scenario requires the Dash diagnostic report schema.' }
    # Preserve old-build validation, but never relabel v6 collection/shooting evidence as Dash gameplay.
    if ($Scenario -eq 'smoke') { return }
    foreach ($values in @($hostValues, $joinValues)) {
        if ($values['scenario'] -ne $Scenario -or $values['fault_profile'] -ne $FaultProfile) {
            throw 'Final report scenario/profile differs from launcher selection.'
        }
    }
    if ($FaultProfile -eq 'peer-silence') {
        foreach ($values in @($hostValues, $joinValues)) {
            if ($values['observed_departure'] -ne 'PEER TIMEOUT' -or $values['expected_departure'] -ne 'PEER TIMEOUT' -or
                $values['peer_silence_activated'] -ne 'true' -or [int]$values['peer_silence_injected_faults'] -le 0 -or
                [int]$values['injected_faults'] -le 0 -or $values['stage'] -notlike 'results-hold-*') {
                throw 'Negative peer-silence profile lacks explicit activation, actual suppressed traffic, or the expected results-stage timeout.'
            }
            $currentPrefix = ''
            $matchingEpochs = 0
            for ($i = 0; $i -lt [int]$values['completed_epoch_count']; $i++) {
                $prefix = "completed_$($i)_"
                if ($values[$prefix + 'connection_id'] -eq $values['connection_id'] -and
                    $values[$prefix + 'epoch'] -eq $values['epoch']) {
                    $currentPrefix = $prefix
                    $matchingEpochs++
                }
            }
            if ($matchingEpochs -ne 1) { throw 'Negative profile must retain exactly one summary for its current confirmed epoch.' }
            $terminalFields = @{
                finish_source = 'finish_source'; finish_tick = 'finish_tick'; finish_checksum = 'checksum'
                final_score_0 = 'score_0'; final_score_1 = 'score_1'; winner = 'winner'; settings_hash = 'settings_hash'
                result_hold_seconds = 'result_hold_seconds'; confirmed_scores = 'score_events'
            }
            foreach ($field in $terminalFields.Keys) {
                $summaryKey = $currentPrefix + $terminalFields[$field]
                if (!$values.ContainsKey($field) -or !$values.ContainsKey($summaryKey) -or $values[$field] -ne $values[$summaryKey]) {
                    throw "Negative profile current-epoch provenance/evidence differs from the confirmed snapshot: $field"
                }
            }
            if ([int]$values[$currentPrefix + 'score_events'] -le 0) {
                throw 'Negative profile lacks an actual confirmed score event.'
            }
        }
        foreach ($field in @('connection_id', 'epoch', 'finish_tick', 'finish_checksum', 'final_score_0', 'final_score_1', 'winner')) {
            if (!$hostValues.ContainsKey($field) -or !$joinValues.ContainsKey($field) -or $hostValues[$field] -ne $joinValues[$field]) {
                throw "Negative profile terminal evidence differs: $field"
            }
        }
        return
    }
    $requiredRematches = if ($Scenario -eq 'lifecycle') { 3 } else { 1 }
    foreach ($values in @($hostValues, $joinValues)) {
        if ($values['stage'] -ne 'complete' -or [int]$values['successful_rematches'] -ne $requiredRematches -or
            [int]$values['completed_epoch_count'] -ne $requiredRematches) {
            throw 'Final report did not complete all required matches and rematches.'
        }
    }
    for ($epoch = 0; $epoch -lt $requiredRematches; $epoch++) {
        $prefix = "completed_$($epoch)_"
        foreach ($field in @('connection_id', 'epoch', 'finish_tick', 'checksum', 'score_0', 'score_1', 'winner', 'settings_hash', 'finish_source')) {
            $key = $prefix + $field
            if (!$hostValues.ContainsKey($key) -or !$joinValues.ContainsKey($key) -or $hostValues[$key] -ne $joinValues[$key]) {
                throw "Peer epoch evidence mismatch or missing field: $key"
            }
        }
        $source = if ($Scenario -eq 'lifecycle' -and $epoch -eq 0) { 'seeded_fixture' } else { 'normal_gameplay' }
        foreach ($values in @($hostValues, $joinValues)) {
            $hold = [double]::Parse($values[$prefix + 'result_hold_seconds'], [Globalization.CultureInfo]::InvariantCulture)
            if ($values[$prefix + 'finish_source'] -ne $source -or $hold -lt 8.0) {
                throw "Epoch $epoch lacks the prescribed source or eight-second result hold."
            }
            if ($source -eq 'normal_gameplay') {
                foreach ($field in @('collections', 'shots', 'dynamic_collisions', 'score_events')) {
                    if ([int]$values[$prefix + $field] -le 0) { throw "Epoch $epoch missing actual gameplay event: $field" }
                }
            }
        }
    }
    if ($hostValues['connection_id'] -ne $joinValues['connection_id'] -or $hostValues['epoch'] -ne $joinValues['epoch']) {
        throw 'Peers finished on different connection identities or epochs.'
    }
    if ($Scenario -eq 'lifecycle') {
        $departingValues = if ($LeaveRole -eq 'host') { $hostValues } else { $joinValues }
        $remainingValues = if ($LeaveRole -eq 'host') { $joinValues } else { $hostValues }
        if ($departingValues['observed_departure'] -ne 'LOCAL LEAVE' -or $remainingValues['observed_departure'] -ne 'PEER LEFT') {
            throw 'Planned departure evidence did not match the designated leaving role.'
        }
        if ($hostValues['connection_id'] -eq $hostValues['completed_0_connection_id'] -or $hostValues['epoch'] -ne '0') {
            throw 'Fresh connection reused the original identity or did not start at epoch zero.'
        }
    }
    if ($FaultProfile -ne 'none' -and ([int]$hostValues['injected_faults'] + [int]$joinValues['injected_faults']) -le 0) {
        throw 'The selected fault profile did not inject any fault.'
    }
}

try {
    # Default diagnostics stay hidden; visible presentation runs require an explicit opt-in.
    $playerWindowStyle = if ($ShowPresentationWindows) { 'Normal' } else { 'Hidden' }
    $hostProcess = Start-Process -FilePath $PlayerPath -ArgumentList (Get-PlayerArguments 'host' $hostResult $hostLog) -WindowStyle $playerWindowStyle -PassThru
    $launchedProcesses += $hostProcess
    $readyDeadline = (Get-Date).AddSeconds($HostStartupTimeoutSeconds)
    while (!(Test-Path -LiteralPath $hostReady) -and (Get-Date) -lt $readyDeadline) {
        $hostProcess.Refresh()
        if ($hostProcess.HasExited) { break }
        Start-Sleep -Milliseconds 100
    }
    if (!(Test-Path -LiteralPath $hostReady)) {
        throw "Host did not become ready within $HostStartupTimeoutSeconds seconds."
    }

    $launcherStage = 'starting-join'
    $joinProcess = Start-Process -FilePath $PlayerPath -ArgumentList (Get-PlayerArguments 'join' $joinResult $joinLog) -WindowStyle $playerWindowStyle -PassThru
    $launchedProcesses += $joinProcess
    if ($ShowPresentationWindows) {
        $launcherStage = 'positioning-owned-presentation-windows'
        $hostWindowTicket = New-DashPresentationWindowTicket -Process $hostProcess -ExpectedPlayerPath $PlayerPath
        $joinWindowTicket = New-DashPresentationWindowTicket -Process $joinProcess -ExpectedPlayerPath $PlayerPath
        $dimensions = $ProbeSize.Split('x')
        $windowObservations = @(Show-DashPresentationPair -FirstTicket $hostWindowTicket -SecondTicket $joinWindowTicket `
            -ExpectedWidth ([int]$dimensions[0]) -ExpectedHeight ([int]$dimensions[1]) -CapturePresentation -ShowPresentationWindows)
        [IO.File]::WriteAllText((Join-Path $resultDir 'presentation-windows.json'),
            ($windowObservations | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
    }
    $launcherStage = 'awaiting-whole-scenario-reports'
    $deadline = (Get-Date).AddSeconds($effectiveTimeoutSeconds)
    while (!(Test-FinalReport $hostResult) -or !(Test-FinalReport $joinResult) -or
        !(Test-ProbeReport 'host') -or !(Test-ProbeReport 'join')) {
        if ((Get-Date) -ge $deadline) { throw "Whole scenario exceeded $effectiveTimeoutSeconds seconds." }
        if ($null -ne $failureReportDeadline -and (Get-Date) -ge $failureReportDeadline) {
            throw "$launcherFailure Peer final report collection exceeded the $failureReportGraceSeconds-second failure grace."
        }
        foreach ($process in $launchedProcesses) {
            $process.Refresh()
            $processReport = if ($process.Id -eq $hostProcess.Id) { $hostResult } else { $joinResult }
            if ($process.HasExited -and !(Test-FinalReport $processReport)) {
                if ($null -eq $failureReportDeadline) {
                    $launcherFailure = "Player process $($process.Id) exited before its final report."
                    $failureReportDeadline = (Get-Date).AddSeconds($failureReportGraceSeconds)
                    $launcherStage = 'collecting-failure-reports'
                }
            }
        }
        foreach ($reportPath in @($hostResult, $joinResult)) {
            if ((Test-FinalReport $reportPath) -and (Get-Content -LiteralPath $reportPath -Raw) -match '(?m)^result=FAIL\r?$') {
                if ($null -eq $failureReportDeadline) {
                    $launcherFailure = "Player reported failure: $reportPath"
                    $failureReportDeadline = (Get-Date).AddSeconds($failureReportGraceSeconds)
                    $launcherStage = 'collecting-failure-reports'
                }
            }
        }
        Start-Sleep -Milliseconds 150
    }
    $launcherStage = 'final-reports-received'
    if ((Get-Content -LiteralPath $hostResult -Raw) -match '(?m)^result=PASS\r?$' -and
        (Get-Content -LiteralPath $joinResult -Raw) -match '(?m)^result=PASS\r?$') {
        Assert-PairEvidence
        Assert-ProbeReports
        $exitStatus = 0
    }
    else { $exitStatus = 1 }
}
catch {
    $launcherFailure = $_.Exception.Message
    Write-Warning $launcherFailure
}
finally {
    # Only Process objects created by this invocation are eligible for cleanup.
    foreach ($process in $launchedProcesses) {
        try {
            $process.Refresh()
            if (!$process.HasExited) { Stop-Process -InputObject $process -Force -ErrorAction SilentlyContinue }
        }
        catch { Write-Warning "Could not close launched process $($process.Id): $($_.Exception.Message)" }
    }
    @(
        "player=$PlayerPath"
        "scenario=$Scenario"
        "fault_profile=$FaultProfile"
        "leave_role=$LeaveRole"
        "pacing_stall_role=$PacingStallRole"
        "silence_role=$SilenceRole"
        "expected_dash_protocol=$expectedDashProtocol"
        'pacing_requested_stall_seconds=0.75'
        "mechanic=$Mechanic"
        "mechanic_seat=$MechanicSeat"
        "expect_version_mismatch=$ExpectVersionMismatch"
        "capture_presentation=$CapturePresentation"
        "show_presentation_windows=$ShowPresentationWindows"
        "probe_size=$ProbeSize"
        "probe_frames=$ProbeFrames"
        'scope=same_machine_two_process'
        'two_computer_validated=false'
        "launcher_stage=$launcherStage"
        "launcher_failure=$launcherFailure"
        "launcher_exit_status=$exitStatus"
        "host_result_exists=$(Test-Path -LiteralPath $hostResult)"
        "join_result_exists=$(Test-Path -LiteralPath $joinResult)"
        "host_final_report_received=$(Test-FinalReport $hostResult)"
        "join_final_report_received=$(Test-FinalReport $joinResult)"
        "failure_report_grace_seconds=$failureReportGraceSeconds"
        "host_log_exists=$(Test-Path -LiteralPath $hostLog)"
        "join_log_exists=$(Test-Path -LiteralPath $joinLog)"
        "drop_first_control_packet=$DropFirstControlPacket"
        "simulation_target_ticks=$SimulationTicks"
        "timeout_seconds=$effectiveTimeoutSeconds"
        "scenario_timeout_seconds=$ScenarioTimeoutSeconds"
        'handshake_stage_timeout_seconds=30'
        'dash_gameplay_stage_timeout_seconds=180'
        'legacy_gameplay_stage_timeout_seconds=120'
        'result_hold_minimum_seconds=8'
        'transition_stage_timeout_seconds=20'
        "host_startup_timeout_seconds=$HostStartupTimeoutSeconds"
        "finished=$(Get-Date -Format o)"
    ) | Set-Content -LiteralPath $summary -Encoding UTF8
}

Write-Host "LAN diagnostic results: $resultDir"
foreach ($reportPath in @($hostResult, $joinResult, $summary)) {
    if (Test-Path -LiteralPath $reportPath) {
        Write-Host ([System.IO.Path]::GetFileName($reportPath))
        Get-Content -LiteralPath $reportPath
    }
}
Write-Host 'Full player logs and .stages trails are retained in the result directory.'
exit $exitStatus
