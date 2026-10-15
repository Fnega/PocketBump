$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LanDiagnosticEvidence.ps1')
# Invented report-validator fixtures only: no Unity, Player, socket, or gameplay is run.
$script:pacingChecks = 0
function Assert-PacingRejected([scriptblock]$Action, [string]$Label) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (!$rejected) { throw "Invalid synthetic pacing fixture accepted: $Label" }
    $script:pacingChecks++
}
function New-PacingReportFixture([string]$Role, [string]$StallRole) {
    $seat = if ($Role -eq 'host') { '0' } else { '1' }
    $values = @{
        report_schema='dash_arena_v1'; protocol_version='7'; result='PASS'; whole_scenario_final='true'
        role=$Role; local_seat=$seat; scenario='pacing'; fault_profile='none'; scope='same_machine_two_process'
        two_computer_validated='false'; action_vocabulary='movement,dash'; presentation_error_count='0'
        rules_hash=('1' * 64); layout_hash=('2' * 64); profile_id='production_v1'; seed_policy='1'; seed='42'
        send_attempts='100'; sent_datagrams='100'; received_datagrams='100'; send_failures='0'
        local_endpoint=('127.0.0.1:2100' + $seat); remote_endpoint='127.0.0.1:21000'
        connection_id='12'; first_connection_id='12'; epoch='0'; terminal_reason=''; lifecycle_phase='Playing'
        stage='complete'; initialization_source='normal_gameplay'; finish_source='not_completed'; completed_epochs='0'
        injected_faults='0'; current_epoch_live_ticks='320'; current_epoch_checksums_since_live='200'
        simulation_tick='501'; confirmed_state_tick='500'; validated_checksums='701'
        'input_pacing.scope'='connection_epoch'; 'input_pacing.connection_id'='12'; 'input_pacing.epoch'='0'
        'input_pacing.simulation_tick'='501'; 'input_pacing.latest_local_input_tick'='502'
        'input_pacing.remote_contiguous_input_tick'='502'; 'input_pacing.latest_remote_input_tick'='502'
        'input_pacing.input_lag_ticks'='0'; 'input_pacing.waiting_for_remote_input'='false'
        'input_pacing.input_wait_count'='3'; 'input_pacing.input_wait_seconds'='0.5'
        'input_pacing.input_wait_current_seconds'='0'; 'input_pacing.input_wait_max_seconds'='0.3'
        'input_pacing.input_progress_age_seconds'='0.001'
        pacing_scope='same_machine_two_process_connection_epoch'; pacing_connection_id='12'; pacing_epoch='0'
        pacing_stall_role=$StallRole; pacing_requested_stall_seconds='0.75'
        pacing_maximum_input_lead_ticks='10'; pacing_rollback_buffer_ticks='12'; pacing_input_delay_ticks='2'
        pacing_stall_method='automatic_ticking_disabled_manual_update_pump'
        pacing_shared_stall_tick='310'; pacing_checkpoint_tick='500'; pacing_checkpoint_checksum='-12345'
        pacing_stall_latest_local_input_tick='311'
        pacing_checkpoint_observed='true'; pacing_recovery_start_confirmed_tick='319'
        pacing_recovery_start_checksums='520'; pacing_recovery_confirmed_ticks='181'; pacing_recovery_validated_checksums='181'
    }
    if ($Role -eq $StallRole) {
        $values.pacing_stall_count='1'; $values.pacing_skipped_fixed_steps='45'; $values.pacing_stall_pump_calls='46'
        $values.pacing_actual_stall_seconds='0.751'; $values.pacing_stall_start_tick='310'; $values.pacing_stall_end_tick='310'
        $values.pacing_wait_observed='false'; $values.pacing_resume_observed='false'
        $values.pacing_wait_observed_during_stall='false'
    } else {
        $values.pacing_stall_count='0'; $values.pacing_skipped_fixed_steps='0'; $values.pacing_stall_pump_calls='0'
        $values.pacing_actual_stall_seconds='0'; $values.pacing_stall_start_tick='-1'; $values.pacing_stall_end_tick='-1'
        $values.pacing_wait_observed='true'; $values.pacing_resume_observed='true'
        $values.pacing_wait_observed_during_stall='true'
        $values.pacing_wait_count_baseline='2'; $values.pacing_wait_count_at_observation='3'
        $values.pacing_observed_wait_seconds='0.2'; $values.pacing_wait_tick='321'; $values.pacing_resume_tick='323'
        $values.pacing_wait_contiguous_tick='311'; $values.pacing_resume_contiguous_tick='315'
    }
    return $values
}
function Assert-PacingPair($First, $Second, [string]$StallRole) {
    Assert-DashPairEvidence $First $Second 'pacing' 'none' 'host' $false $false '' 0 $StallRole
}
foreach ($stallRole in @('host', 'join')) {
    $first = New-PacingReportFixture 'host' $stallRole
    $second = New-PacingReportFixture 'join' $stallRole
    Assert-PacingPair $first $second $stallRole
    $script:pacingChecks++
    foreach ($mutation in @(
        @('pacing_stall_count', '0'), @('pacing_stall_count', '2'), @('pacing_skipped_fixed_steps', '0'),
        @('pacing_skipped_fixed_steps', '0.5'), @('pacing_stall_pump_calls', '0'),
        @('pacing_actual_stall_seconds', '0'), @('pacing_actual_stall_seconds', '0.749'),
        @('pacing_actual_stall_seconds', '2'), @('pacing_actual_stall_seconds', 'NaN'),
        @('pacing_stall_start_tick', '309'), @('pacing_stall_end_tick', '311')
    )) {
        $badFirst = $first.Clone(); $badSecond = $second.Clone()
        $injector = if ($stallRole -eq 'host') { $badFirst } else { $badSecond }
        $injector[$mutation[0]] = $mutation[1]
        Assert-PacingRejected { Assert-PacingPair $badFirst $badSecond $stallRole } ('injector ' + ($mutation -join '='))
    }
    foreach ($mutation in @(
        @('pacing_stall_count', '1'), @('pacing_skipped_fixed_steps', '1'), @('pacing_stall_pump_calls', '1'),
        @('pacing_wait_observed', 'false'), @('pacing_resume_observed', 'false'),
        @('pacing_wait_observed_during_stall', 'false'), @('pacing_wait_tick', '324'),
        @('pacing_wait_tick', '323'),
        @('pacing_wait_contiguous_tick', '312'),
        @('pacing_wait_count_at_observation', '2'), @('pacing_wait_count_baseline', '3'),
        @('pacing_observed_wait_seconds', '0'), @('pacing_observed_wait_seconds', '0.9'),
        @('pacing_wait_tick', '309'), @('pacing_resume_tick', '319'), @('pacing_resume_tick', '600'),
        @('pacing_resume_contiguous_tick', '311'), @('pacing_resume_contiguous_tick', '600'),
        @('input_pacing.waiting_for_remote_input', 'true'), @('input_pacing.input_wait_count', '0')
    )) {
        $badFirst = $first.Clone(); $badSecond = $second.Clone()
        $observer = if ($stallRole -eq 'host') { $badSecond } else { $badFirst }
        $observer[$mutation[0]] = $mutation[1]
        Assert-PacingRejected { Assert-PacingPair $badFirst $badSecond $stallRole } ('observer ' + ($mutation -join '='))
    }
    # Corrupt BOTH reports identically: peer equality alone must not establish recovery.
    foreach ($mutation in @(
        @('pacing_scope', 'physical_lan'), @('pacing_connection_id', '13'), @('pacing_epoch', '1'),
        @('pacing_stall_latest_local_input_tick', '312'),
        @('pacing_maximum_input_lead_ticks', '12'), @('pacing_rollback_buffer_ticks', '14'), @('pacing_input_delay_ticks', '0'),
        @('pacing_stall_role', 'neither'), @('pacing_requested_stall_seconds', '0'),
        @('pacing_stall_method', 'state_teleport_fixture'), @('pacing_checkpoint_observed', 'false'),
        @('pacing_checkpoint_tick', '429'), @('pacing_checkpoint_tick', '501'),
        @('pacing_checkpoint_checksum', 'NaN'), @('pacing_recovery_confirmed_ticks', '119'),
        @('pacing_recovery_validated_checksums', '119'), @('pacing_recovery_start_confirmed_tick', '0'),
        @('pacing_recovery_start_checksums', '519'), @('injected_faults', '1'),
        @('terminal_reason', 'INPUT HOLE'), @('terminal_reason', 'INPUT PROGRESS TIMEOUT'),
        @('presentation_error_count', '1'), @('initialization_source', 'labeled_fixture'),
        @('input_pacing.scope', 'process_lifetime'), @('input_pacing.connection_id', '13'),
        @('input_pacing.epoch', '1'), @('input_pacing.simulation_tick', '502'),
        @('input_pacing.input_wait_seconds', '-1'), @('input_pacing.input_wait_max_seconds', '0.6'),
        @('input_pacing.input_wait_current_seconds', '0.1'), @('input_pacing.input_progress_age_seconds', 'NaN'),
        @('input_pacing.input_lag_ticks', '1'), @('input_pacing.remote_contiguous_input_tick', '503')
    )) {
        $badFirst = $first.Clone(); $badSecond = $second.Clone()
        $badFirst[$mutation[0]] = $mutation[1]; $badSecond[$mutation[0]] = $mutation[1]
        Assert-PacingRejected { Assert-PacingPair $badFirst $badSecond $stallRole } ('matching corruption ' + ($mutation -join '='))
    }
    foreach ($key in @('pacing_checkpoint_checksum', 'pacing_shared_stall_tick', 'pacing_stall_latest_local_input_tick')) {
        $bad = $second.Clone(); $bad[$key] = '600'
        Assert-PacingRejected { Assert-PacingPair $first $bad $stallRole } ('peer divergence ' + $key)
    }
    foreach ($key in @('pacing_checkpoint_observed', 'pacing_recovery_validated_checksums', 'pacing_stall_latest_local_input_tick',
            'input_pacing.scope', 'input_pacing.input_wait_count', 'input_pacing.latest_local_input_tick')) {
        $bad = $second.Clone(); $bad.Remove($key)
        Assert-PacingRejected { Assert-PacingPair $first $bad $stallRole } ('missing field ' + $key)
    }
    Assert-PacingRejected { Assert-DashPairEvidence $first $second 'pacing' 'none' 'host' $false $true '' 0 $stallRole } 'mixed handshake-loss injection'
    $starvedFirst = $first.Clone(); $starvedSecond = $second.Clone()
    foreach ($starved in @($starvedFirst, $starvedSecond)) {
        # Regression shape observed in actual 0.11: shared confirmed checkpoint agrees
        # and confirmation progresses, but newly validated checksums never increase.
        $starved['validated_checksums'] = $starved['pacing_recovery_start_checksums']
        $starved['pacing_recovery_validated_checksums'] = '0'
    }
    Assert-PacingRejected { Assert-PacingPair $starvedFirst $starvedSecond $stallRole } 'matching confirmed recovery cannot mask checksum starvation'
}
# Validate a current normal-case metrics group without introducing a pacing test claim.
$snapshot = New-PacingReportFixture 'host' 'host'
$snapshot['input_pacing.waiting_for_remote_input']='true'
$snapshot['input_pacing.input_wait_current_seconds']='0.1'
Assert-DashInputPacingSnapshot $snapshot
$script:pacingChecks++
$snapshot['input_pacing.input_wait_seconds']='0.29999'
Assert-DashInputPacingSnapshot $snapshot
$script:pacingChecks++
Write-Output "PASS: $script:pacingChecks synthetic pacing report-validator fixtures. No Player/gameplay/UDP acceptance is claimed."
