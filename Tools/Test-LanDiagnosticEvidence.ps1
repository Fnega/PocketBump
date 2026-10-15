$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LanDiagnosticEvidence.ps1')
$checks = 0
function Assert-Rejected([scriptblock]$Action, [string]$Label) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (!$rejected) { throw "Invalid algorithm fixture was accepted: $Label" }
    $script:checks++
}
function New-ReportFixture([string]$Role) {
    # Synthetic validator fixture only; NOT gameplay, UDP, or release acceptance evidence.
    $seat = if ($Role -eq 'host') { '0' } else { '1' }
    $endpoint = if ($Role -eq 'host') { '127.0.0.1:20000' } else { '127.0.0.1:20001' }
    $remote = if ($Role -eq 'host') { '127.0.0.1:20001' } else { '127.0.0.1:20000' }
    $values = @{
        report_schema='dash_arena_v1'; protocol_version='7'; result='PASS'; whole_scenario_final='true';
        role=$Role; local_seat=$seat; scenario='full-match'; fault_profile='none'; scope='same_machine_two_process';
        two_computer_validated='false'; action_vocabulary='movement,dash'; presentation_error_count='0';
        rules_hash=('1' * 64); layout_hash=('2' * 64); profile_id='production_v1'; seed_policy='1'; seed='43';
        send_attempts='100'; sent_datagrams='100'; received_datagrams='100'; send_failures='0';
        local_endpoint=$endpoint; remote_endpoint=$remote; first_socket_endpoint=$endpoint;
        injected_faults='0'; fault_required_sender='none'; local_fault_injection_required='false';
        connection_id='12'; first_connection_id='12'; epoch='1';
        terminal_reason=''; lifecycle_phase='Playing';
        stage='complete'; completed_epochs='1'; successful_rematches='1'; initialization_source='normal_gameplay';
        current_epoch_live_ticks='140'; current_epoch_checksums_since_live='130';
        'epoch.0.connection_id'='12'; 'epoch.0.epoch'='0'; 'epoch.0.seed'='42';
        'epoch.0.rules_hash'=('1' * 64); 'epoch.0.layout_hash'=('2' * 64);
        'epoch.0.initialization_source'='normal_gameplay'; 'epoch.0.finish_source'='normal_gameplay';
        'epoch.0.finish_tick'='2807'; 'epoch.0.checksum'='54321'; 'epoch.0.score0'='8'; 'epoch.0.score1'='1';
        'epoch.0.winner'='0'; 'epoch.0.settings_hash'='33'; 'epoch.0.hold_seconds'='8.010';
        'epoch.0.unilateral_verified'='true'; 'epoch.0.validated_checksums'='990'; 'epoch.0.socket_endpoint'=$endpoint;
        'epoch.0.dash0'='8'; 'epoch.0.dash1'='1'; 'epoch.0.collision_goals0'='8'; 'epoch.0.collision_goals1'='1';
        'epoch.0.goals'='9'; 'epoch.0.winners'='1'; confirmed_event_count='37'
    }
    # Complete synthetic event grammar: nine separate Gate -> Dash -> ball Impact -> Goal rallies,
    # with one P2 goal and eight P1 goals, followed by the actual tuple's MatchOver tick.
    # These invented coordinates/timestamps test the validator, NOT the simulation/transport.
    for ($rally = 0; $rally -lt 9; $rally++) {
        $scorer = if ($rally -eq 1) { 1 } else { 0 }
        $gateTick = 179 + 312 * $rally
        $goalY = if ($scorer -eq 0) { '-36.3' } else { '36.3' }
        Add-EventFixture $values ($rally * 4) $gateTick -1 -1 'GateOpened' '0' '0'
        Add-EventFixture $values ($rally * 4 + 1) ($gateTick + 10) $scorer $scorer 'DashStarted' '0' '3'
        Add-EventFixture $values ($rally * 4 + 2) ($gateTick + 12) $scorer 2 'Impact' '0' '0'
        Add-EventFixture $values ($rally * 4 + 3) ($gateTick + 72) $scorer 0 'Goal' '0' $goalY
    }
    Add-EventFixture $values 36 2807 0 -1 'MatchOver' '0' '0'
    return $values
}
function Add-EventFixture($Values, [int]$Index, [int]$Tick, [int]$Seat, [int]$Entity, [string]$Kind, [string]$X, [string]$Y) {
    $prefix = "event.$Index."
    $Values[$prefix + 'connection_id']='12'; $Values[$prefix + 'epoch']='0'; $Values[$prefix + 'tick']=[string]$Tick
    $Values[$prefix + 'seat']=[string]$Seat; $Values[$prefix + 'entity']=[string]$Entity; $Values[$prefix + 'kind']=$Kind
    $Values[$prefix + 'ordinal']='0'; $Values[$prefix + 'rules_hash']=('1' * 64)
    $Values[$prefix + 'x']=$X; $Values[$prefix + 'y']=$Y
}
function Remove-EventFixture($Values, [int]$Index) {
    $count = [int]$Values['confirmed_event_count']
    for ($n = $Index; $n -lt $count - 1; $n++) {
        foreach ($field in @('connection_id', 'epoch', 'tick', 'seat', 'entity', 'kind', 'ordinal', 'rules_hash', 'x', 'y')) {
            $Values["event.$n.$field"] = $Values['event.' + ($n + 1) + '.' + $field]
        }
    }
    $Values['confirmed_event_count'] = [string]($count - 1)
}
function Assert-SamePairMutationRejected([scriptblock]$Mutation, [string]$Label) {
    $badFirst = New-ReportFixture 'host'; $badSecond = New-ReportFixture 'join'
    & $Mutation $badFirst
    & $Mutation $badSecond
    Assert-Rejected { Assert-DashPairEvidence $badFirst $badSecond 'full-match' 'none' 'host' } $Label
}
$first = New-ReportFixture 'host'
$second = New-ReportFixture 'join'
Assert-DashPairEvidence $first $second 'full-match' 'none' 'host'
$checks++
foreach ($mutation in @(
    @('whole_scenario_final', 'false'), @('rules_hash', 'bad'), @('protocol_version', '6'),
    @('epoch.0.finish_source', 'seeded_fixture'), @('epoch.0.hold_seconds', '7.99'),
    @('epoch.0.dash1', '0'), @('epoch.0.collision_goals1', '0'), @('epoch.0.winner', '1'),
    @('epoch.0.score0', '1'), @('epoch.0.score1', '7'), @('epoch.0.goals', '10'),
    @('epoch.0.checksum', '98765'), @('epoch.0.socket_endpoint', '127.0.0.1:9999'),
    @('successful_rematches', '0'), @('current_epoch_checksums_since_live', '119'),
    @('presentation_error_count', '1'), @('send_failures', '1'), @('local_seat', '0'),
    @('terminal_reason', 'DESYNC'), @('lifecycle_phase', 'Failed'),
    @('confirmed_event_count', '0'), @('event.0.tick', '999'), @('event.0.rules_hash', ('3' * 64))
)) {
    $bad = $second.Clone(); $bad[$mutation[0]] = $mutation[1]
    Assert-Rejected { Assert-DashPairEvidence $first $bad 'full-match' 'none' 'host' } $mutation[0]
}
$missing = $second.Clone(); $missing.Remove('epoch.0.dash1')
Assert-Rejected { Assert-DashPairEvidence $first $missing 'full-match' 'none' 'host' } 'missing numeric field'
Assert-Rejected { Assert-DashPairEvidence $first $second 'full-match' 'none' 'host' $false $true } 'no actual handshake fault'
$negativeFirst = $first.Clone(); $negativeSecond = $second.Clone()
foreach ($values in @($negativeFirst, $negativeSecond)) {
    $values.fault_profile = 'peer-silence'; $values.stage = 'results-hold-epoch-0';
    $values.fault_required_sender = 'both'; $values.local_fault_injection_required = 'true';
    $values.peer_silence_activated = 'true'; $values.peer_silence_injected_faults = '2'; $values.injected_faults = '2';
    $values.expected_departure = 'PEER TIMEOUT'; $values.observed_departure = 'PEER TIMEOUT';
    $values.terminal_reason = 'PEER TIMEOUT'; $values.epoch = '0'; $values.seed = '42';
    $values.finish_source = 'normal_gameplay'; $values.finish_tick = '2807'; $values.finish_checksum = '54321';
    $values.final_score_0 = '8'; $values.final_score_1 = '1'; $values.winner = '0';
    $values.result_hold_seconds = '6.123456'; $values['epoch.0.hold_seconds'] = '6.123'
}
Assert-DashPairEvidence $negativeFirst $negativeSecond 'full-match' 'peer-silence' 'host'
$checks++
foreach ($mutation in @(@('peer_silence_injected_faults', '0'), @('stage', 'gameplay-epoch-0'),
        @('terminal_reason', 'INPUT HOLE'), @('finish_tick', '999'), @('seed', '99'))) {
    $bad = $negativeSecond.Clone(); $bad[$mutation[0]] = $mutation[1]
    Assert-Rejected { Assert-DashPairEvidence $negativeFirst $bad 'full-match' 'peer-silence' 'host' } $mutation[0]
}
Assert-SamePairMutationRejected { param($v) Remove-EventFixture $v 1 } 'both peers lost P1 Dash evidence'
Assert-SamePairMutationRejected { param($v) Remove-EventFixture $v 6 } 'both peers lost the P2 ball-impact link'
Assert-SamePairMutationRejected { param($v) Remove-EventFixture $v 7 } 'both peers lost the P2 goal despite retained counters'
Assert-SamePairMutationRejected { param($v) Remove-EventFixture $v 36 } 'both peers lost terminal MatchOver'
Assert-SamePairMutationRejected { param($v) $v['event.7.rules_hash']=('3' * 64) } 'matching wrong event hashes do not identify the epoch'
Assert-SamePairMutationRejected { param($v) $v['event.3.y']='36.3' } 'matching goal is in wrong defended goal'
Assert-SamePairMutationRejected { param($v) $v['event.3.x']='4' } 'matching goal is outside real goal opening'
Assert-SamePairMutationRejected { param($v) $v['event.6.entity']='1' } 'P2 player contact is not scoring-ball evidence'
Assert-SamePairMutationRejected { param($v) $v['event.6.tick']='520' } 'P2 impact is after its eighteen-tick Dash window'
Assert-SamePairMutationRejected { param($v) $v['event.36.seat']='1' } 'matching terminal event contradicts winner tuple'
Assert-SamePairMutationRejected { param($v) $v['event.36.tick']='2806' } 'matching terminal event contradicts finish tick'
Assert-SamePairMutationRejected { param($v) $v['event.35.tick']='2808' } 'completed event exceeds retained finish tick'
Assert-SamePairMutationRejected { param($v) $v['event.2.kind']='FakeImpact' } 'matching unknown event kind'
Assert-SamePairMutationRejected { param($v) $v['event.2.ordinal']='0.5' } 'matching fractional event identity'
Assert-SamePairMutationRejected { param($v) $v['event.2.x']='NaN' } 'matching nonfinite position payload'
Assert-SamePairMutationRejected { param($v) $v['epoch.0.dash0']='9' } 'matching summary counters lack actual event count'
Assert-SamePairMutationRejected {
    param($v)
    # Old weak positive: both summaries claimed nine goals, but only a winner was actually retained.
    $v['confirmed_event_count']='1'
    Add-EventFixture $v 0 2807 0 -1 'MatchOver' '0' '0'
} 'both peers retain only MatchOver while claiming an entire natural match'

# Every original profile retains its protocol sender meaning. These are complete
# synthetic pair reports, not a real dropped packet or successful gameplay run.
$senderMatrix = @{
    'drop-finish'='both'; 'drop-finish-ack'='both'; 'drop-consent'='both'; 'drop-consent-ack'='both'
    'drop-offer'='host'; 'drop-prepared'='join'; 'drop-commit'='host'; 'drop-commit-ack'='join'
    'drop-start'='host'; 'drop-startup-input'='both'; 'drop-startup-ack'='both'
    'duplicate-reorder'='both'; 'stale-epoch'='both'; 'peer-silence'='both'
}
foreach ($profile in $senderMatrix.Keys) {
    $profileFirst = if ($profile -eq 'peer-silence') { $negativeFirst.Clone() } else { New-ReportFixture 'host' }
    $profileSecond = if ($profile -eq 'peer-silence') { $negativeSecond.Clone() } else { New-ReportFixture 'join' }
    $sender = $senderMatrix[$profile]
    foreach ($values in @($profileFirst, $profileSecond)) {
        $required = $sender -eq 'both' -or $sender -eq $values.role
        $values.fault_profile = $profile
        $values.injected_faults = if ($required) { '2' } else { '0' }
        $values.fault_required_sender = $sender
        $values.local_fault_injection_required = if ($required) { 'true' } else { 'false' }
    }
    Assert-DashPairEvidence $profileFirst $profileSecond 'full-match' $profile 'host'
    $checks++
    foreach ($seat in @(0, 1)) {
        $badFirst = $profileFirst.Clone(); $badSecond = $profileSecond.Clone()
        $bad = @($badFirst, $badSecond)[$seat]
        $bad.injected_faults = if ($bad.injected_faults -eq '0') { '2' } else { '0' }
        Assert-Rejected { Assert-DashPairEvidence $badFirst $badSecond 'full-match' $profile 'host' } "wrong $profile injection on seat $seat"
        $badFirst = $profileFirst.Clone(); $badSecond = $profileSecond.Clone()
        @($badFirst, $badSecond)[$seat].injected_faults = '0.5'
        Assert-Rejected { Assert-DashPairEvidence $badFirst $badSecond 'full-match' $profile 'host' } "fractional $profile injection on seat $seat"
    }
    $badFirst = $profileFirst.Clone(); $badSecond = $profileSecond.Clone()
    $badFirst.injected_faults='0'; $badSecond.injected_faults='0'
    Assert-Rejected { Assert-DashPairEvidence $badFirst $badSecond 'full-match' $profile 'host' } "zero pair injection for $profile"
    $badFirst = $profileFirst.Clone(); $badSecond = $profileSecond.Clone()
    $badFirst.fault_required_sender='none'; $badSecond.fault_required_sender='none'
    Assert-Rejected { Assert-DashPairEvidence $badFirst $badSecond 'full-match' $profile 'host' } "self-declared sender cannot override $profile"
    if ($sender -ne 'both') {
        $badFirst = $profileFirst.Clone(); $badSecond = $profileSecond.Clone()
        $badFirst.injected_faults=$profileSecond.injected_faults; $badSecond.injected_faults=$profileFirst.injected_faults
        Assert-Rejected { Assert-DashPairEvidence $badFirst $badSecond 'full-match' $profile 'host' } "only wrong peer injected $profile"
    }
    # Missing additive declarations do not exempt a legacy report from the real role/count contract.
    foreach ($values in @($profileFirst, $profileSecond)) {
        $values.Remove('fault_required_sender'); $values.Remove('local_fault_injection_required')
    }
    Assert-DashPairEvidence $profileFirst $profileSecond 'full-match' $profile 'host'
    $checks++
}
$handshakeFirst = New-ReportFixture 'host'; $handshakeSecond = New-ReportFixture 'join'
foreach ($values in @($handshakeFirst, $handshakeSecond)) {
    $values.injected_faults='2'; $values.fault_required_sender='both'; $values.local_fault_injection_required='true'
}
Assert-DashPairEvidence $handshakeFirst $handshakeSecond 'full-match' 'none' 'host' $false $true
$checks++
foreach ($seat in @(0, 1)) {
    $badFirst = $handshakeFirst.Clone(); $badSecond = $handshakeSecond.Clone()
    @($badFirst, $badSecond)[$seat].injected_faults='0'
    Assert-Rejected { Assert-DashPairEvidence $badFirst $badSecond 'full-match' 'none' 'host' $false $true } "handshake injection absent on seat $seat"
}
Assert-Rejected { Assert-DashFaultSenders $first $second 'unknown' $false } 'unknown fault profile'
Assert-Rejected { Assert-DashFaultSenders $first $second 'drop-offer' $true } 'combined injection profiles'
Write-Output "PASS: $checks pure report-validator algorithm fixtures. No gameplay or socket acceptance is claimed."
