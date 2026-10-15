# Pure evidence validation. Dot-sourced by the launcher and its algorithm-fixture tests.
# This file never starts/stops players or changes reports.
function Assert-DashField($Values, [string]$Key, [string]$Expected) {
    if (!$Values.ContainsKey($Key) -or $Values[$Key] -cne $Expected) {
        throw "Missing or unexpected diagnostic field '$Key': expected '$Expected', got '$($Values[$Key])'."
    }
}

function Get-DashNumber($Values, [string]$Key) {
    $parsed = 0.0
    if (!$Values.ContainsKey($Key) -or ![double]::TryParse($Values[$Key],
            [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$parsed) -or
        [double]::IsNaN($parsed) -or [double]::IsInfinity($parsed)) {
        throw "Missing or invalid numeric diagnostic field '$Key'."
    }
    return $parsed
}

function Assert-DashMinimum($Values, [string]$Key, [double]$Minimum) {
    if ((Get-DashNumber $Values $Key) -lt $Minimum) { throw "Diagnostic milestone '$Key' is below $Minimum." }
}

function Get-DashInteger($Values, [string]$Key, [double]$Minimum, [double]$Maximum) {
    $value = Get-DashNumber $Values $Key
    if ($value -ne [Math]::Truncate($value) -or $value -lt $Minimum -or $value -gt $Maximum) {
        throw "Diagnostic field '$Key' is not an integer in [$Minimum, $Maximum]."
    }
    return [long]$value
}

function Assert-DashMatchingFields($First, $Second, [string[]]$Fields) {
    foreach ($key in $Fields) {
        if (!$First.ContainsKey($key) -or !$Second.ContainsKey($key) -or $First[$key] -cne $Second[$key]) {
            throw "Missing or different peer evidence: $key."
        }
    }
}

function Assert-DashInputPacingSnapshot($Values, [string]$Prefix = 'input_pacing.', [string]$IdentityPrefix = '') {
    Assert-DashField $Values ($Prefix + 'scope') 'connection_epoch'
    foreach ($key in @('connection_id', 'epoch')) {
        Assert-DashField $Values ($Prefix + $key) $Values[$IdentityPrefix + $key]
    }
    $tick = Get-DashInteger $Values ($Prefix + 'simulation_tick') 0 ([int]::MaxValue)
    if ($IdentityPrefix -eq '') { Assert-DashField $Values ($Prefix + 'simulation_tick') $Values['simulation_tick'] }
    $null = Get-DashInteger $Values ($Prefix + 'latest_local_input_tick') -1 ([int]::MaxValue)
    $contiguous = Get-DashInteger $Values ($Prefix + 'remote_contiguous_input_tick') -1 ([int]::MaxValue)
    $latest = Get-DashInteger $Values ($Prefix + 'latest_remote_input_tick') -1 ([int]::MaxValue)
    $lag = Get-DashInteger $Values ($Prefix + 'input_lag_ticks') 0 ([int]::MaxValue)
    $count = Get-DashInteger $Values ($Prefix + 'input_wait_count') 0 ([int]::MaxValue)
    $total = Get-DashNumber $Values ($Prefix + 'input_wait_seconds')
    $current = Get-DashNumber $Values ($Prefix + 'input_wait_current_seconds')
    $maximum = Get-DashNumber $Values ($Prefix + 'input_wait_max_seconds')
    $age = Get-DashNumber $Values ($Prefix + 'input_progress_age_seconds')
    $waiting = $Values[$Prefix + 'waiting_for_remote_input']
    if ($waiting -cnotin @('true', 'false') -or $contiguous -gt $latest -or
        ($tick -gt 0 -and $lag -ne [Math]::Max(0, $tick - $contiguous)) -or
        [Math]::Min([Math]::Min($total, $current), [Math]::Min($maximum, $age)) -lt 0 -or
        # Sequential realtime property reads may straddle one clock quantization edge.
        $total + .001 -lt $maximum -or $maximum + .001 -lt $current -or
        ($waiting -eq 'false' -and $current -ne 0) -or ($waiting -eq 'true' -and $count -eq 0) -or
        ($count -eq 0 -and ($total -ne 0 -or $maximum -ne 0))) {
        throw "Inconsistent input-pacing snapshot: $Prefix."
    }
}

function Assert-DashNaturalEpoch($Values, [string]$Prefix, [bool]$RequireHealthyHold) {
    Assert-DashField $Values ($Prefix + 'initialization_source') 'normal_gameplay'
    Assert-DashField $Values ($Prefix + 'finish_source') 'normal_gameplay'
    foreach ($field in @('finish_tick', 'validated_checksums', 'dash0', 'dash1', 'collision_goals0', 'collision_goals1')) {
        Assert-DashMinimum $Values ($Prefix + $field) 1
    }
    Assert-DashField $Values ($Prefix + 'winners') '1'
    $score0 = Get-DashNumber $Values ($Prefix + 'score0')
    $score1 = Get-DashNumber $Values ($Prefix + 'score1')
    $winner = Get-DashNumber $Values ($Prefix + 'winner')
    if ($score0 -ne [Math]::Truncate($score0) -or $score1 -ne [Math]::Truncate($score1) -or
        [Math]::Min($score0, $score1) -lt 0 -or [Math]::Max($score0, $score1) -lt 8 -or
        [Math]::Abs($score0 - $score1) -lt 2 -or
        ($score0 -gt $score1 -and $winner -ne 0) -or ($score1 -gt $score0 -and $winner -ne 1) -or
        (Get-DashNumber $Values ($Prefix + 'goals')) -ne $score0 + $score1) {
        throw "Epoch '$Prefix' lacks a legal eight-point win-by-two finish with actual goal evidence."
    }
    if ($RequireHealthyHold) {
        Assert-DashMinimum $Values ($Prefix + 'hold_seconds') 8
        Assert-DashField $Values ($Prefix + 'unilateral_verified') 'true'
    }
}

function Assert-DashFaultSenders($HostValues, $JoinValues, [string]$Profile, [bool]$DroppedHandshake) {
    # Independent protocol policy: never trust a report's self-declared required role.
    $senders = @{
        'none'='none'; 'drop-finish'='both'; 'drop-finish-ack'='both'
        'drop-consent'='both'; 'drop-consent-ack'='both'; 'drop-offer'='host'
        'drop-prepared'='join'; 'drop-commit'='host'; 'drop-commit-ack'='join'; 'drop-start'='host'
        'drop-startup-input'='both'; 'drop-startup-ack'='both'; 'duplicate-reorder'='both'
        'stale-epoch'='both'; 'peer-silence'='both'
    }
    if (!$senders.ContainsKey($Profile) -or ($DroppedHandshake -and $Profile -ne 'none')) {
        throw 'Unknown or combined diagnostic fault profile cannot establish actual injection.'
    }
    $sender = if ($DroppedHandshake) { 'both' } else { $senders[$Profile] }
    $peers = @($HostValues, $JoinValues)
    for ($seat = 0; $seat -lt 2; $seat++) {
        $values = $peers[$seat]
        $required = $sender -eq 'both' -or $sender -eq @('host','join')[$seat]
        if ($required -or $Profile -ne 'none' -or $values.ContainsKey('injected_faults')) {
            $injected = Get-DashInteger $values 'injected_faults' 0 ([int]::MaxValue)
            if (($required -and $injected -eq 0) -or (!$required -and $injected -ne 0)) {
                throw "Profile '$Profile' actual injection does not match its prescribed sending role: $(@('host','join')[$seat])."
            }
        }
        # Additive declarations are optional for archived reports, but if present must
        # agree. Required traffic/count checks above remain mandatory for fault claims.
        if ($values.ContainsKey('fault_required_sender') -or $values.ContainsKey('local_fault_injection_required')) {
            Assert-DashField $values 'fault_required_sender' $sender
            Assert-DashField $values 'local_fault_injection_required' $(if ($required) { 'true' } else { 'false' })
        }
    }
}

function Assert-DashFaultPairEvidence($HostValues, $JoinValues, [string]$SilenceRole) {
    if ($SilenceRole -cnotin @('host', 'join')) { throw 'A directional silence role is required.' }
    Assert-DashMatchingFields $HostValues $JoinValues @('connection_id', 'epoch', 'seed', 'first_connection_id',
        'finish_tick', 'finish_checksum', 'final_score_0', 'final_score_1', 'winner')
    $finishFields = @('connection_id', 'epoch', 'seed', 'rules_hash', 'layout_hash', 'initialization_source',
        'finish_source', 'finish_tick', 'checksum', 'score0', 'score1', 'winner', 'settings_hash',
        'dash0', 'dash1', 'collision_goals0', 'collision_goals1', 'goals', 'winners') | ForEach-Object { 'epoch.0.' + $_ }
    Assert-DashMatchingFields $HostValues $JoinValues $finishFields
    foreach ($values in @($HostValues, $JoinValues)) {
        $silent = $values['role'] -ceq $SilenceRole
        Assert-DashField $values 'stage' 'fault-evidence-complete'
        Assert-DashField $values 'fault_evidence_scope' 'same_machine_two_process_confirmed_result_hold'
        Assert-DashField $values 'fault_injection_method' 'suppress_selected_process_outbound_after_confirmed_result'
        Assert-DashField $values 'fault_silence_role' $SilenceRole
        Assert-DashField $values 'fault_silence_activated' $(if ($silent) { 'true' } else { 'false' })
        Assert-DashField $values 'fault_required_sender' $SilenceRole
        Assert-DashField $values 'local_fault_injection_required' $(if ($silent) { 'true' } else { 'false' })
        Assert-DashField $values 'terminal_reason' $(if ($silent) { 'PEER FAULT' } else { 'PEER TIMEOUT' })
        Assert-DashField $values 'failure_origin' $(if ($silent) { 'peer' } else { 'local' })
        Assert-DashField $values 'peer_failure_reason' $(if ($silent) { 'PEER TIMEOUT' } else { '' })
        Assert-DashField $values 'fault_disconnect_seconds' '5'
        Assert-DashField $values 'fault_input_progress_timeout_seconds' '5'
        Assert-DashField $values 'fault_timeline_retention_seconds' '20'
        Assert-DashField $values 'fault_timeline_retained' 'true'
        Assert-DashMinimum $values 'fault_frozen_hold_seconds' 21
        $null = Get-DashInteger $values 'fault_timeline_samples' 5 81
        $injected = Get-DashInteger $values 'fault_injected_datagrams' 0 ([int]::MaxValue)
        if (($silent -and $injected -le 0) -or (!$silent -and $injected -ne 0) -or
            $injected -ne (Get-DashInteger $values 'injected_faults' 0 ([int]::MaxValue))) {
            throw 'Directional evidence must prove actual suppression only on the selected sender.'
        }
        foreach ($key in @('fault_timeline_sha256', 'fault_first_report_sha256', 'fault_retained_report_sha256')) {
            if ($values[$key] -cnotmatch '^[0-9A-F]{64}$') { throw "Missing exact fault evidence hash: $key." }
        }
        foreach ($key in @('fault_first_report', 'fault_retained_report')) {
            if ([string]::IsNullOrWhiteSpace($values[$key])) { throw "Missing retained fault evidence file: $key." }
        }
        Assert-DashField $values 'profile_id' 'production_v1'
        Assert-DashField $values 'epoch' '0'
        Assert-DashField $values 'completed_epochs' '1'
        Assert-DashField $values 'initialization_source' 'normal_gameplay'
        Assert-DashField $values 'finish_source' 'normal_gameplay'
        Assert-DashField $values 'connection_id' $values['first_connection_id']
        Assert-DashField $values 'local_seat' $(if ($values['role'] -eq 'host') { '0' } else { '1' })
        Assert-DashNaturalEpoch $values 'epoch.0.' $false
        $mapping = @{connection_id='connection_id'; epoch='epoch'; seed='seed'; rules_hash='rules_hash'; layout_hash='layout_hash';
            finish_tick='finish_tick'; finish_checksum='checksum'; final_score_0='score0'; final_score_1='score1'; winner='winner'}
        foreach ($key in $mapping.Keys) { Assert-DashField $values ('epoch.0.' + $mapping[$key]) $values[$key] }
    }
    $hostEvents = @(Get-DashCompletedEventSignatures $HostValues)
    $joinEvents = @(Get-DashCompletedEventSignatures $JoinValues)
    if ($hostEvents.Count -eq 0 -or $hostEvents.Count -ne $joinEvents.Count -or
        [string]::Join("`n", $hostEvents) -cne [string]::Join("`n", $joinEvents)) {
        throw 'Directional fault baseline lacks identical actual confirmed natural-finish event trails.'
    }
}

function Assert-DashFaultReportFiles($Values, [string]$DiagnosticReportPath, [string]$BuildGuid) {
    $snapshots = @()
    $capturedTimes = @()
    $finishTick = Get-DashInteger $Values 'finish_tick' 0 ([int]::MaxValue - 1)
    $finishChecksum = Get-DashInteger $Values 'finish_checksum' ([int]::MinValue) ([int]::MaxValue)
    foreach ($kind in @('first', 'retained')) {
        $path = [IO.Path]::GetFullPath($DiagnosticReportPath + '.fault-' + $kind + '.json')
        if ($Values['fault_' + $kind + '_report'] -cne $path -or !(Test-Path -LiteralPath $path -PathType Leaf) -or
            (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -cne $Values['fault_' + $kind + '_report_sha256']) {
            throw 'Fault evidence file is missing, redirected or changed after its diagnostic report.'
        }
        $snapshot = Get-Content -LiteralPath $path -Raw -Encoding UTF8 | ConvertFrom-Json
        $numbers = @{}
        foreach ($key in @('process_id', 'write_sequence', 'simulation_tick', 'checksum_tick', 'checksum')) {
            $numbers[$key] = [Convert]::ToString($snapshot.$key, [Globalization.CultureInfo]::InvariantCulture)
        }
        $null = Get-DashInteger $numbers 'process_id' 1 ([int]::MaxValue)
        $null = Get-DashInteger $numbers 'write_sequence' 1 ([int]::MaxValue)
        # FinishTick identifies the executed terminal tick; simulation state stores
        # the next tick. Production reports request its confirmed post-tick checksum.
        $null = Get-DashInteger $numbers 'simulation_tick' ($finishTick + 1) ($finishTick + 1)
        $null = Get-DashInteger $numbers 'checksum_tick' $finishTick $finishTick
        $null = Get-DashInteger $numbers 'checksum' $finishChecksum $finishChecksum
        $capturedAt = [DateTimeOffset]::MinValue
        if ($snapshot.event_name -cne ('development_fault-' + $kind) -or
            ![DateTimeOffset]::TryParseExact([string]$snapshot.captured_utc, 'O', [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$capturedAt) -or $capturedAt.Offset -ne [TimeSpan]::Zero) {
            throw 'Fault report requires its exact capture event and a valid UTC capture time.'
        }
        $capturedTimes += $capturedAt
        if ($snapshot.schema -cne 'pocketbump_lan_release_v1' -or $snapshot.evidence_version -ne 2 -or
            $snapshot.development_build -ne $true -or $snapshot.protocol_version -ne 8 -or !$snapshot.timeline_frozen -or
            $snapshot.build_guid.Replace('-', '') -ine $BuildGuid.Replace('-', '') -or
            [string]$snapshot.match_id -cne $Values['connection_id'] -or [string]$snapshot.epoch -cne $Values['epoch'] -or
            $snapshot.role -cne $Values['role'] -or $snapshot.failure_reason -cne $Values['terminal_reason'] -or
            $snapshot.failure_origin -cne $Values['failure_origin'] -or $snapshot.peer_failure_reason -cne $Values['peer_failure_reason'] -or
            $snapshot.rules_hash -cne $Values['rules_hash'] -or $snapshot.layout_hash -cne $Values['layout_hash'] -or
            $snapshot.timeline.Count -ne (Get-DashInteger $Values 'fault_timeline_samples' 5 81)) {
            throw 'Retained production report identity, terminal cause or frozen evidence differs from diagnostic claims.'
        }
        $first = $snapshot.timeline[0]; $last = $snapshot.timeline[-1]
        if ($first.failure_reason -or $first.elapsed_seconds -ge $last.elapsed_seconds -or
            $last.failure_reason -cne $snapshot.failure_reason -or $last.failure_origin -cne $snapshot.failure_origin -or
            $last.peer_failure_reason -cne $snapshot.peer_failure_reason -or $last.receive_polls -le 0 -or
            ($snapshot.failure_origin -eq 'local' -and $last.accepted_peer_age_seconds -lt 5)) {
            throw 'Production timeline lacks pre-failure history or the observed timeout sample.'
        }
        $snapshots += $snapshot
    }
    if ($snapshots[0].process_id -ne $snapshots[1].process_id -or
        $snapshots[1].write_sequence -le $snapshots[0].write_sequence -or
        ($capturedTimes[1] - $capturedTimes[0]).TotalSeconds -lt 20.9) {
        throw 'Retained evidence must be a later capture from the same process at least 20.9 seconds after the first.'
    }
    if (($snapshots[0].timeline | ConvertTo-Json -Depth 8 -Compress) -cne ($snapshots[1].timeline | ConvertTo-Json -Depth 8 -Compress) -or
        $snapshots[0].report_id -cne $snapshots[1].report_id -or
        $snapshots[0].simulation_tick -ne $snapshots[1].simulation_tick -or
        $snapshots[0].checksum -ne $snapshots[1].checksum) {
        throw 'Frozen production timeline or result changed during the post-fault hold.'
    }
}

function Assert-DashPairEvidence($HostValues, $JoinValues, [string]$SelectedScenario,
    [string]$SelectedFault, [string]$SelectedLeaveRole, [bool]$ExpectMismatch = $false,
    [bool]$DroppedHandshake = $false, [string]$SelectedMechanic = '', [int]$SelectedMechanicSeat = 0,
    [string]$SelectedPacingStallRole = 'host', [string]$SelectedSilenceRole = 'host', [int]$ExpectedProtocol = 0) {
    $peers = @($HostValues, $JoinValues)
    # Retain read-only validation of historical protocol-7 fixtures and reports.
    # A current launcher also binds this value to the selected build-info.json.
    $protocol = Get-DashInteger $HostValues 'protocol_version' 7 8
    if ($ExpectedProtocol -ne 0 -and $protocol -ne $ExpectedProtocol) { throw 'Diagnostic protocol differs from the selected build.' }
    for ($i = 0; $i -lt $peers.Count; $i++) {
        $values = $peers[$i]
        Assert-DashField $values 'report_schema' 'dash_arena_v1'
        Assert-DashField $values 'protocol_version' ([string]$protocol)
        Assert-DashField $values 'result' 'PASS'
        Assert-DashField $values 'whole_scenario_final' 'true'
        Assert-DashField $values 'role' (@('host', 'join')[$i])
        Assert-DashField $values 'scenario' $SelectedScenario
        Assert-DashField $values 'fault_profile' $SelectedFault
        Assert-DashField $values 'scope' 'same_machine_two_process'
        Assert-DashField $values 'two_computer_validated' 'false'
        Assert-DashField $values 'action_vocabulary' 'movement,dash'
        Assert-DashField $values 'presentation_error_count' '0'
        foreach ($key in @('rules_hash', 'layout_hash')) {
            if ($values[$key] -cnotmatch '^[0-9a-f]{64}$') { throw "Missing canonical content identity: $key." }
        }
        Assert-DashMinimum $values 'send_attempts' 1
        Assert-DashMinimum $values 'sent_datagrams' 1
        Assert-DashMinimum $values 'received_datagrams' 1
        Assert-DashField $values 'send_failures' '0'
        foreach ($key in @('local_endpoint', 'remote_endpoint')) {
            if ([string]::IsNullOrWhiteSpace($values[$key])) { throw "Missing actual socket endpoint: $key." }
        }
        # Retained pre-pacing v7 reports remain historical evidence, not rewritten data.
        # New reports carry this group; a pacing claim always requires the whole group.
        if ($values.ContainsKey('input_pacing.scope') -or $SelectedScenario -eq 'pacing') {
            Assert-DashInputPacingSnapshot $values
        }
    }
    Assert-DashMatchingFields $HostValues $JoinValues @('rules_hash', 'layout_hash', 'profile_id', 'seed_policy')
    if ($SelectedScenario -ne 'fault-evidence') { Assert-DashFaultSenders $HostValues $JoinValues $SelectedFault $DroppedHandshake }
    if ($ExpectMismatch) {
        foreach ($values in $peers) {
            Assert-DashField $values 'expects_version_mismatch' 'true'
            Assert-DashField $values 'terminal_reason' 'SETTINGS MISMATCH'
            Assert-DashField $values 'simulation_tick' '0'
            Assert-DashField $values 'completed_epochs' '0'
        }
        Assert-DashField $HostValues 'advertised_protocol_version' ([string]$protocol)
        Assert-DashField $JoinValues 'advertised_protocol_version' ([string]($protocol - 1))
        return
    }
    if ($SelectedScenario -eq 'fault-evidence') {
        if ($protocol -ne 8 -or $SelectedFault -ne 'none' -or $DroppedHandshake -or $ExpectMismatch) {
            throw 'Directional fault evidence requires an independent protocol-8 scenario.'
        }
        Assert-DashFaultPairEvidence $HostValues $JoinValues $SelectedSilenceRole
        return
    }
    if ($SelectedFault -ne 'peer-silence') {
        foreach ($values in $peers) {
            Assert-DashField $values 'terminal_reason' ''
            Assert-DashField $values 'lifecycle_phase' 'Playing'
        }
    }
    Assert-DashMatchingFields $HostValues $JoinValues @('connection_id', 'epoch', 'seed', 'first_connection_id')
    for ($i = 0; $i -lt $peers.Count; $i++) { Assert-DashField $peers[$i] 'local_seat' ([string]$i) }
    if ($SelectedScenario -eq 'mechanics') {
        Assert-DashMechanicPairEvidence $HostValues $JoinValues $SelectedMechanic $SelectedMechanicSeat
        return
    }
    Assert-DashField $HostValues 'profile_id' 'production_v1'
    Assert-DashField $JoinValues 'profile_id' 'production_v1'
    if ($SelectedScenario -eq 'pacing') {
        if ($SelectedFault -ne 'none' -or $DroppedHandshake -or $ExpectMismatch) {
            throw 'Pacing must not substitute for, or mix with, packet fault profiles.'
        }
        Assert-DashPacingPairEvidence $HostValues $JoinValues $SelectedPacingStallRole
        return
    }
    if ($SelectedScenario -eq 'smoke') {
        foreach ($values in $peers) {
            Assert-DashField $values 'stage' 'complete'
            Assert-DashField $values 'finish_source' 'not_completed'
            Assert-DashField $values 'initialization_source' 'normal_gameplay'
            Assert-DashField $values 'completed_epochs' '0'
            Assert-DashMinimum $values 'current_epoch_live_ticks' 120
            Assert-DashMinimum $values 'current_epoch_checksums_since_live' 120
            Assert-DashMinimum $values 'smoke_player_0_max_displacement' .25
            Assert-DashMinimum $values 'smoke_player_1_max_displacement' .25
            if ((Get-DashNumber $values 'simulation_tick') -lt (Get-DashNumber $values 'smoke_target_tick')) {
                throw 'Smoke tick threshold was not reached.'
            }
        }
        return
    }
    $negative = $SelectedFault -eq 'peer-silence'
    $required = if ($negative) { 1 } elseif ($SelectedScenario -eq 'lifecycle') { 3 } else { 1 }
    foreach ($values in $peers) {
        Assert-DashField $values 'completed_epochs' ([string]$required)
        if ($negative) {
            Assert-DashField $values 'peer_silence_activated' 'true'
            Assert-DashMinimum $values 'peer_silence_injected_faults' 1
            Assert-DashField $values 'expected_departure' 'PEER TIMEOUT'
            Assert-DashField $values 'observed_departure' 'PEER TIMEOUT'
            Assert-DashField $values 'terminal_reason' 'PEER TIMEOUT'
            if ($values['stage'] -notlike 'results-hold-*') { throw 'Negative fault occurred outside its armed result phase.' }
            $mapping = @{ connection_id='connection_id'; epoch='epoch'; seed='seed'; rules_hash='rules_hash';
                layout_hash='layout_hash'; finish_source='finish_source'; finish_tick='finish_tick';
                finish_checksum='checksum'; final_score_0='score0'; final_score_1='score1'; winner='winner' }
            foreach ($key in $mapping.Keys) {
                if (!$values.ContainsKey($key) -or $values[$key] -ne $values['epoch.0.' + $mapping[$key]]) {
                    throw "Negative profile current tuple does not match its retained confirmed snapshot: $key."
                }
            }
            if ([Math]::Abs((Get-DashNumber $values 'result_hold_seconds') -
                    (Get-DashNumber $values 'epoch.0.hold_seconds')) -gt .001) {
                throw 'Negative result hold differs from its retained snapshot.'
            }
        } else {
            Assert-DashField $values 'stage' 'complete'
            Assert-DashField $values 'successful_rematches' ([string]$required)
            Assert-DashField $values 'initialization_source' 'normal_gameplay'
            Assert-DashMinimum $values 'current_epoch_live_ticks' 120
            Assert-DashMinimum $values 'current_epoch_checksums_since_live' 120
        }
    }
    for ($epoch = 0; $epoch -lt $required; $epoch++) {
        $prefix = "epoch.$epoch."
        $fields = @('connection_id', 'epoch', 'seed', 'rules_hash', 'layout_hash', 'initialization_source', 'finish_source',
            'finish_tick', 'checksum', 'score0', 'score1', 'winner', 'settings_hash', 'dash0', 'dash1',
            'collision_goals0', 'collision_goals1', 'goals', 'winners') | ForEach-Object { $prefix + $_ }
        Assert-DashMatchingFields $HostValues $JoinValues $fields
        foreach ($values in $peers) {
            Assert-DashNaturalEpoch $values $prefix (!$negative)
            Assert-DashField $values ($prefix + 'epoch') ([string]$epoch)
            Assert-DashField $values ($prefix + 'connection_id') $values['first_connection_id']
            Assert-DashField $values ($prefix + 'rules_hash') $values['rules_hash']
            Assert-DashField $values ($prefix + 'layout_hash') $values['layout_hash']
            Assert-DashField $values ($prefix + 'socket_endpoint') $values['first_socket_endpoint']
        }
    }
    $hostEvents = @(Get-DashCompletedEventSignatures $HostValues)
    $joinEvents = @(Get-DashCompletedEventSignatures $JoinValues)
    if ($hostEvents.Count -eq 0 -or $hostEvents.Count -ne $joinEvents.Count -or
        [string]::Join("`n", $hostEvents) -cne [string]::Join("`n", $joinEvents)) {
        throw 'Completed-epoch confirmed event identities/outcomes differ between peers or are missing.'
    }
    if (!$negative -and $SelectedScenario -eq 'lifecycle') {
        $departing = if ($SelectedLeaveRole -eq 'host') { $HostValues } else { $JoinValues }
        $remaining = if ($SelectedLeaveRole -eq 'host') { $JoinValues } else { $HostValues }
        Assert-DashField $departing 'observed_departure' 'LOCAL LEAVE'
        Assert-DashField $departing 'expected_departure' 'LOCAL LEAVE'
        Assert-DashField $remaining 'observed_departure' 'PEER LEFT'
        Assert-DashField $remaining 'expected_departure' 'PEER LEFT'
        Assert-DashField $HostValues 'epoch' '0'
        if ($HostValues['connection_id'] -eq $HostValues['first_connection_id']) {
            throw 'Fresh join reused the original connection identity.'
        }
    } elseif (!$negative) {
        Assert-DashField $HostValues 'epoch' ([string]$required)
        Assert-DashField $HostValues 'connection_id' $HostValues['first_connection_id']
    }
}

function Assert-DashPacingPairEvidence($HostValues, $JoinValues, [string]$StallRole) {
    if ($StallRole -cnotin @('host', 'join')) { throw 'Explicit host/join pacing stall role is required.' }
    $peers = @($HostValues, $JoinValues)
    $injector = if ($StallRole -eq 'host') { $HostValues } else { $JoinValues }
    $observer = if ($StallRole -eq 'host') { $JoinValues } else { $HostValues }
    Assert-DashMatchingFields $HostValues $JoinValues @('pacing_connection_id', 'pacing_epoch', 'pacing_shared_stall_tick', 'pacing_stall_latest_local_input_tick',
        'pacing_checkpoint_tick', 'pacing_checkpoint_checksum', 'pacing_stall_role', 'pacing_requested_stall_seconds')
    foreach ($values in $peers) {
        Assert-DashField $values 'stage' 'complete'
        Assert-DashField $values 'finish_source' 'not_completed'
        Assert-DashField $values 'initialization_source' 'normal_gameplay'
        Assert-DashField $values 'completed_epochs' '0'
        Assert-DashField $values 'epoch' '0'
        Assert-DashField $values 'injected_faults' '0'
        Assert-DashField $values 'pacing_scope' 'same_machine_two_process_connection_epoch'
        Assert-DashField $values 'pacing_stall_method' 'automatic_ticking_disabled_manual_update_pump'
        Assert-DashField $values 'pacing_stall_role' $StallRole
        Assert-DashField $values 'pacing_maximum_input_lead_ticks' '10'
        Assert-DashField $values 'pacing_rollback_buffer_ticks' '12'
        Assert-DashField $values 'pacing_input_delay_ticks' '2'
        Assert-DashField $values 'pacing_connection_id' $values['connection_id']
        Assert-DashField $values 'pacing_epoch' $values['epoch']
        Assert-DashField $values 'pacing_checkpoint_observed' 'true'
        Assert-DashField $values 'input_pacing.waiting_for_remote_input' 'false'
        Assert-DashMinimum $values 'current_epoch_live_ticks' 120
        Assert-DashMinimum $values 'current_epoch_checksums_since_live' 120
        if ((Get-DashNumber $values 'pacing_requested_stall_seconds') -ne .75 -or
            (Get-DashInteger $values 'connection_id' ([int]::MinValue) ([int]::MaxValue)) -eq 0) {
            throw 'Pacing does not identify the prescribed real 0.75 second connected-player stall.'
        }
        $sharedTick = Get-DashInteger $values 'pacing_shared_stall_tick' 300 ([int]::MaxValue)
        $producerLastInput = Get-DashInteger $values 'pacing_stall_latest_local_input_tick' ($sharedTick + 1) ($sharedTick + 1)
        $checkpoint = Get-DashInteger $values 'pacing_checkpoint_tick' ($sharedTick + 120) ([int]::MaxValue)
        $null = Get-DashInteger $values 'pacing_checkpoint_checksum' ([int]::MinValue) ([int]::MaxValue)
        $confirmed = Get-DashInteger $values 'confirmed_state_tick' $checkpoint ([int]::MaxValue)
        $validated = Get-DashInteger $values 'validated_checksums' 120 ([int]::MaxValue)
        $startConfirmed = Get-DashInteger $values 'pacing_recovery_start_confirmed_tick' ($sharedTick - 1) ($checkpoint - 1)
        $startChecksums = Get-DashInteger $values 'pacing_recovery_start_checksums' 0 $validated
        $progress = Get-DashInteger $values 'pacing_recovery_confirmed_ticks' 120 ([int]::MaxValue)
        $checksums = Get-DashInteger $values 'pacing_recovery_validated_checksums' 120 ([int]::MaxValue)
        if ($confirmed - $startConfirmed -ne $progress -or $validated - $startChecksums -ne $checksums -or
            (Get-DashInteger $values 'simulation_tick' 1 ([int]::MaxValue)) -le $confirmed) {
            throw 'Recovery counters do not recompute from the actual post-resume baselines.'
        }
    }
    Assert-DashField $injector 'pacing_stall_count' '1'
    $null = Get-DashInteger $injector 'pacing_skipped_fixed_steps' 1 ([int]::MaxValue)
    $null = Get-DashInteger $injector 'pacing_stall_pump_calls' 1 ([int]::MaxValue)
    Assert-DashField $injector 'pacing_stall_start_tick' $injector['pacing_shared_stall_tick']
    Assert-DashField $injector 'pacing_stall_end_tick' $injector['pacing_stall_start_tick']
    $seconds = Get-DashNumber $injector 'pacing_actual_stall_seconds'
    if ($seconds -lt .75 -or $seconds -ge 2) { throw 'Actual stall was absent, too short, or not bounded.' }
    Assert-DashField $injector 'pacing_wait_observed' 'false'
    Assert-DashField $injector 'pacing_wait_observed_during_stall' 'false'
    Assert-DashField $injector 'pacing_resume_observed' 'false'
    foreach ($key in @('pacing_stall_count', 'pacing_skipped_fixed_steps', 'pacing_stall_pump_calls', 'pacing_actual_stall_seconds')) {
        Assert-DashField $observer $key '0'
    }
    Assert-DashField $observer 'pacing_stall_start_tick' '-1'
    Assert-DashField $observer 'pacing_stall_end_tick' '-1'
    Assert-DashField $observer 'pacing_wait_observed' 'true'
    Assert-DashField $observer 'pacing_wait_observed_during_stall' 'true'
    Assert-DashField $observer 'pacing_resume_observed' 'true'
    $baseline = Get-DashInteger $observer 'pacing_wait_count_baseline' 0 ([int]::MaxValue)
    $observedCount = Get-DashInteger $observer 'pacing_wait_count_at_observation' ($baseline + 1) ([int]::MaxValue)
    $null = Get-DashInteger $observer 'input_pacing.input_wait_count' $observedCount ([int]::MaxValue)
    $waitSeconds = Get-DashNumber $observer 'pacing_observed_wait_seconds'
    if ($waitSeconds -le 0 -or $waitSeconds -gt (Get-DashNumber $observer 'input_pacing.input_wait_max_seconds') + .00001) {
        throw 'Observer lacks an actual positive-duration input wait.'
    }
    $waitTick = Get-DashInteger $observer 'pacing_wait_tick' ([long]$observer['pacing_shared_stall_tick']) ([int]::MaxValue)
    $resumeTick = Get-DashInteger $observer 'pacing_resume_tick' ($waitTick + 1) ([int]::MaxValue)
    $waitPrefix = Get-DashInteger $observer 'pacing_wait_contiguous_tick' 0 ($waitTick - 1)
    $resumePrefix = Get-DashInteger $observer 'pacing_resume_contiguous_tick' ($waitPrefix + 1) ([int]::MaxValue)
    # Production delay remains two, rollback remains twelve; pacing lead is only TEN
    # so the slow producer's older confirmed checksum remains inside the receiver window.
    # The observer must exhaust exactly the frozen producer's prefix DURING that stall,
    # not claim an unrelated ordinary wait after the producer was already resumed.
    if ($waitPrefix -ne [long]$observer['pacing_stall_latest_local_input_tick'] -or
        $waitTick -ne $waitPrefix + [long]$observer['pacing_maximum_input_lead_ticks'] -or
        $resumeTick -gt (Get-DashNumber $observer 'pacing_checkpoint_tick') -or
        $resumePrefix -gt (Get-DashNumber $observer 'input_pacing.remote_contiguous_input_tick')) {
        throw 'Claimed resume is not followed by the final confirmed input/checksum progress.'
    }
}

function Assert-DashMechanicPairEvidence($HostValues, $JoinValues, [string]$Mechanic, [int]$Seat) {
    $requirements = @{
        'stun-recovery' = @('stunned', 'stun_recovered')
        'green-impact' = @('green_pickup', 'green_enhanced_impact')
        'purple-immunity' = @('purple_pickup', 'purple_dynamic_immunity', 'purple_pillar_immunity')
        'blue-teleport' = @('blue_pickup', 'blue_safe_relocation')
        'blue-no-safe' = @('blue_pickup', 'blue_no_safe_destination')
        'fall-respawn' = @('fall_without_score', 'safe_respawn_without_score')
        'wall-player-repair' = @('dash_started', 'wall_broken', 'player_occupied_repair_pending', 'wall_restored_after_clearance')
        'wall-ball-repair' = @('dash_started', 'wall_broken', 'ball_occupied_repair_pending', 'ball_cleared_by_impact', 'wall_restored_after_clearance')
    }
    if (!$requirements.ContainsKey($Mechanic)) { throw 'Unknown declared mechanic scenario.' }
    $required = $requirements[$Mechanic]
    $eventKinds = @{
        stunned='Stunned'; green_pickup='Pickup'; green_enhanced_impact='Impact'; purple_pickup='Pickup';
        purple_dynamic_immunity='Impact'; purple_pillar_immunity='PillarHit'; blue_pickup='Pickup'; blue_safe_relocation='Teleport';
        blue_no_safe_destination='TeleportFailed'; fall_without_score='Fell'; safe_respawn_without_score='Respawned';
        dash_started='DashStarted'; wall_broken='WallBroken'; player_occupied_repair_pending='WallRepairPending';
        ball_occupied_repair_pending='WallRepairPending'; ball_cleared_by_impact='Impact'; wall_restored_after_clearance='WallRepaired'
    }
    $peerMilestones = @()
    Assert-DashMatchingFields $HostValues $JoinValues @('fixture_id', 'fixture_initial_checksum', 'fixture_prebroken_wall_id')
    foreach ($values in @($HostValues, $JoinValues)) {
        Assert-DashField $values 'profile_id' 'mechanic_fixture'
        Assert-DashField $values 'stage' 'complete'
        Assert-DashField $values 'initialization_source' 'labeled_fixture'
        Assert-DashField $values 'finish_source' 'not_completed'
        Assert-DashField $values 'completed_epochs' '0'
        Assert-DashField $values 'mechanic_case' $Mechanic
        Assert-DashField $values 'mechanic_seat' ([string]$Seat)
        Assert-DashField $values 'fixture_id' ("dash_mechanic_v1_" + $Mechanic + '_seat_' + $Seat)
        Assert-DashField $values 'mechanic_pass' 'true'
        Assert-DashField $values 'mechanic_invalid_reason' ''
        Assert-DashField $values 'required_milestones' ([string]::Join(',', $required))
        Assert-DashField $values 'missing_milestones' ''
        Assert-DashField $values 'epoch' '0'
        Assert-DashMinimum $values 'confirmed_state_tick' 300
        Assert-DashMinimum $values 'validated_checksums' 120
        $null = Get-DashInteger $values 'fixture_initial_checksum' ([int]::MinValue) ([int]::MaxValue)
        $count = Get-DashInteger $values 'milestone_count' $required.Count 32
        $events = Get-DashInteger $values 'confirmed_event_count' 1 512
        $milestones = @{}
        for ($n = 0; $n -lt $count; $n++) {
            $prefix = "milestone.$n."
            $name = $values[$prefix + 'name']
            if ([string]::IsNullOrWhiteSpace($name) -or $milestones.ContainsKey($name)) { throw 'Missing or duplicated mechanic milestone name.' }
            $tick = Get-DashInteger $values ($prefix + 'tick') 0 (Get-DashNumber $values 'confirmed_state_tick')
            Assert-DashField $values ($prefix + 'epoch') '0'
            Assert-DashField $values ($prefix + 'seat') ([string]$Seat)
            Assert-DashField $values ($prefix + 'rules_hash') $values.rules_hash
            if ([string]::IsNullOrWhiteSpace($values[$prefix + 'outcome'])) { throw 'A mechanic milestone has no actual outcome.' }
            $entity = Get-DashInteger $values ($prefix + 'entity') -1 ([int]::MaxValue)
            $milestones[$name] = @{ tick=$tick; entity=$entity; outcome=$values[$prefix + 'outcome'] }
            if ($required -contains $name -and $eventKinds.ContainsKey($name)) {
                $matchesEvent = $false
                $eventSeat = if ($name -like '*repair_pending' -or $name -eq 'wall_restored_after_clearance') { '-1' } else { [string]$Seat }
                for ($i = 0; $i -lt $events; $i++) {
                    $eventPrefix = "event.$i."
                    if ($values[$eventPrefix + 'tick'] -eq [string]$tick -and $values[$eventPrefix + 'epoch'] -eq '0' -and
                        $values[$eventPrefix + 'connection_id'] -eq $values.connection_id -and
                        $values[$eventPrefix + 'kind'] -ceq $eventKinds[$name] -and $values[$eventPrefix + 'seat'] -eq $eventSeat -and
                        $values[$eventPrefix + 'entity'] -eq [string]$entity -and $values[$eventPrefix + 'rules_hash'] -ceq $values.rules_hash) {
                        $matchesEvent = $true; break
                    }
                }
                if (!$matchesEvent) { throw "Mechanic milestone '$name' has no matching corrected confirmed event." }
            }
        }
        foreach ($name in $required) { if (!$milestones.ContainsKey($name)) { throw "Missing actual mechanic milestone: $name." } }
        if ($Mechanic -eq 'stun-recovery' -and $milestones.stun_recovered.tick - $milestones.stunned.tick -lt 36) {
            throw 'Stun recovery was reported before the 36-tick timer elapsed.'
        }
        if ($Mechanic -eq 'fall-respawn' -and $milestones.safe_respawn_without_score.tick - $milestones.fall_without_score.tick -lt 180) {
            throw 'Respawn was reported before the 180-tick timer elapsed.'
        }
        if ($Mechanic -eq 'green-impact') {
            Assert-DashField $values 'comparison_type' 'readonly_previous_confirmed_state_actual_inputs_remove_green_only'
            if ((Get-DashNumber $values 'green_actual_ball_speed') -le (Get-DashNumber $values 'green_unbuffed_counterfactual_ball_speed')) {
                throw 'A green pickup without observed enhanced impact cannot pass.'
            }
            $comparisonTick = Get-DashInteger $values 'green_comparison_tick' $milestones.green_pickup.tick $milestones.green_enhanced_impact.tick
            if ($comparisonTick -ne $milestones.green_enhanced_impact.tick) {
                throw 'Green counterfactual and enhanced-impact milestone must describe the same confirmed Impact tick.'
            }
            foreach ($inputKey in @('green_comparison_input0', 'green_comparison_input1')) { $null = Get-DashInteger $values $inputKey 0 65535 }
        }
        if ($Mechanic -like 'wall-*') {
            $pending = if ($Mechanic -eq 'wall-player-repair') { $milestones.player_occupied_repair_pending } else { $milestones.ball_occupied_repair_pending }
            $startTick = if ($Mechanic -eq 'wall-player-repair') { $milestones.wall_broken.tick } else { -1 }
            if ($pending.tick - $startTick -lt 600 -or $milestones.wall_restored_after_clearance.tick -le $pending.tick -or
                $milestones.wall_restored_after_clearance.entity -ne $pending.entity) {
                throw 'Wall repair lacks its 600 advancing ticks, actual occupancy delay, or later clearance/restoration.'
            }
            if ($Mechanic -eq 'wall-ball-repair') {
                Assert-DashField $values 'fixture_prebroken_wall_id' ([string]$pending.entity)
                if ($milestones.wall_broken.entity -eq $pending.entity -or $milestones.ball_cleared_by_impact.tick -le $pending.tick) {
                    throw 'Fixture-prebroken repair wall and actual Dash-broken wall must remain separate evidence.'
                }
            }
        }
        $peerMilestones += ,$milestones
    }
    foreach ($name in $required) {
        Assert-DashMatchingFields $peerMilestones[0][$name] $peerMilestones[1][$name] @('tick', 'entity', 'outcome')
    }
}

function Get-DashCompletedEventSignatures($Values) {
    $count = Get-DashInteger $Values 'confirmed_event_count' 1 4096
    $completed = @{}
    $epochCount = Get-DashInteger $Values 'completed_epochs' 1 16
    for ($n = 0; $n -lt $epochCount; $n++) {
        $prefix = "epoch.$n."
        $connection = Get-DashInteger $Values ($prefix + 'connection_id') ([int]::MinValue) ([int]::MaxValue)
        if ($connection -eq 0) { throw 'Completed epoch has no connection identity.' }
        $epoch = Get-DashInteger $Values ($prefix + 'epoch') 0 ([int]::MaxValue)
        $key = "$connection/$epoch"
        if ($completed.ContainsKey($key)) { throw 'A completed epoch summary is duplicated.' }
        $completed[$key] = @{
            Prefix=$prefix; LastTick=-1L; LastOrdinal=-1L; Dash=@(0, 0); LastDash=@(-100000L, -100000L);
            CollisionGoals=@(0, 0); Scores=@(0, 0); Goals=0; Winners=0; BallLink=$null; RallyOpen=$false;
            LastGoalTick=-1L; FinishTick=(Get-DashInteger $Values ($prefix + 'finish_tick') 0 ([int]::MaxValue));
            RulesHash=$Values[$prefix + 'rules_hash']; EventCount=0
        }
    }
    $identities = @{}
    $signatures = New-Object 'System.Collections.Generic.List[string]'
    $kinds = @('DashStarted', 'Impact', 'Goal', 'Fell', 'Respawned', 'Stunned', 'WallHit', 'WallBroken',
        'WallRepairPending', 'WallRepaired', 'Pickup', 'Teleport', 'TeleportFailed', 'GateOpened', 'MatchOver', 'PillarHit')
    for ($n = 0; $n -lt $count; $n++) {
        $prefix = "event.$n."
        $fields = @('connection_id', 'epoch', 'tick', 'seat', 'entity', 'kind', 'ordinal', 'rules_hash', 'x', 'y')
        $parts = foreach ($field in $fields) {
            if (!$Values.ContainsKey($prefix + $field)) { throw "Missing confirmed event field: $prefix$field." }
            $Values[$prefix + $field]
        }
        $connection = Get-DashInteger $Values ($prefix + 'connection_id') ([int]::MinValue) ([int]::MaxValue)
        if ($connection -eq 0) { throw 'A confirmed event has no connection identity.' }
        $epoch = Get-DashInteger $Values ($prefix + 'epoch') 0 ([int]::MaxValue)
        $tick = Get-DashInteger $Values ($prefix + 'tick') 0 ([int]::MaxValue)
        $seat = Get-DashInteger $Values ($prefix + 'seat') -1 1
        $entity = Get-DashInteger $Values ($prefix + 'entity') -1 ([int]::MaxValue)
        $ordinal = Get-DashInteger $Values ($prefix + 'ordinal') 0 ([int]::MaxValue)
        $x = Get-DashNumber $Values ($prefix + 'x')
        $y = Get-DashNumber $Values ($prefix + 'y')
        $kind = $Values[$prefix + 'kind']
        if ($kinds -cnotcontains $kind) { throw 'Unknown confirmed event kind.' }
        $key = "$connection/$epoch"
        if (!$completed.ContainsKey($key)) { continue }
        $summary = $completed[$key]
        if ($Values[$prefix + 'rules_hash'] -cne $summary.RulesHash) {
            throw 'Confirmed event rules hash does not identify its own completed epoch.'
        }
        $identity = "$connection/$epoch/$tick/$seat/$entity/$kind/$ordinal"
        if ($identities.ContainsKey($identity)) { throw 'A confirmed event identity was counted more than once.' }
        $identities[$identity] = $true
        if ($tick -gt $summary.FinishTick -or $tick -lt $summary.LastTick -or
            ($tick -eq $summary.LastTick -and $ordinal -le $summary.LastOrdinal) -or $summary.Winners -ne 0) {
            throw 'Completed event trail is reordered, extends past its finish, or continues after MatchOver.'
        }
        $summary.LastTick = $tick; $summary.LastOrdinal = $ordinal; $summary.EventCount++
        # Natural launcher runs the approved 8-point, one-ball profile: body IDs 0/1, ball body 2 / ball index 0.
        switch -CaseSensitive ($kind) {
            'GateOpened' {
                if ($seat -ne -1 -or $entity -ne -1 -or $summary.RallyOpen) { throw 'Invalid or repeated rally-opening event.' }
                $summary.RallyOpen = $true; $summary.BallLink = $null
            }
            'DashStarted' {
                if ($seat -lt 0 -or $entity -ne $seat -or !$summary.RallyOpen) { throw 'Dash milestone is not a player action in a live rally.' }
                $summary.Dash[$seat]++; $summary.LastDash[$seat] = $tick
            }
            'Impact' {
                if ($seat -lt 0 -or $entity -le $seat -or $entity -gt 2) { throw 'Impact milestone has invalid participant identities.' }
                if ($entity -eq 2) {
                    if (!$summary.RallyOpen) { throw 'Free-ball impact occurs outside a live rally.' }
                    $delta = $tick - $summary.LastDash[$seat]
                    $summary.BallLink = @{ Seat=$seat; Tick=$tick; Dash=($delta -ge 0 -and $delta -lt 18) }
                }
            }
            'Goal' {
                if ($seat -lt 0 -or $entity -ne 0 -or !$summary.RallyOpen -or [Math]::Abs($x) -gt 1.7001 -or
                    ($seat -eq 0 -and $y -ge -36) -or ($seat -eq 1 -and $y -le 36)) {
                    throw 'Goal evidence does not cross the proper real goal in a live one-ball rally.'
                }
                $summary.Goals++; $summary.Scores[$seat]++; $summary.LastGoalTick = $tick
                if ($null -ne $summary.BallLink -and $summary.BallLink.Seat -eq $seat -and $summary.BallLink.Dash) {
                    $summary.CollisionGoals[$seat]++
                }
                $summary.BallLink = $null; $summary.RallyOpen = $false
            }
            'MatchOver' {
                if ($seat -lt 0 -or $entity -ne -1 -or $summary.RallyOpen -or $tick -ne $summary.FinishTick -or
                    $seat -ne (Get-DashInteger $Values ($summary.Prefix + 'winner') 0 1) -or
                    $tick -ne $summary.LastGoalTick + 60) {
                    throw 'MatchOver does not match the retained winner/tick after the final goal presentation.'
                }
                $summary.Winners++
            }
        }
        $signatures.Add([string]::Join('/', $parts))
    }
    foreach ($summary in $completed.Values) {
        $prefix = $summary.Prefix
        if ($summary.EventCount -gt 512 -or $summary.Winners -ne 1) { throw 'Completed epoch is missing its bounded complete terminal event trail.' }
        for ($seat = 0; $seat -lt 2; $seat++) {
            if ($summary.Dash[$seat] -ne (Get-DashInteger $Values ($prefix + "dash$seat") 1 512) -or
                $summary.CollisionGoals[$seat] -ne (Get-DashInteger $Values ($prefix + "collision_goals$seat") 1 512) -or
                $summary.Scores[$seat] -ne (Get-DashInteger $Values ($prefix + "score$seat") 0 512)) {
                throw "Epoch '$prefix' counters/scores disagree with its actual Dash-impact-goal event chain."
            }
        }
        if ($summary.Goals -ne (Get-DashInteger $Values ($prefix + 'goals') 1 512) -or
            $summary.Winners -ne (Get-DashInteger $Values ($prefix + 'winners') 1 1)) {
            throw "Epoch '$prefix' goal/winner count disagrees with its actual event trail."
        }
    }
    return $signatures.ToArray()
}
