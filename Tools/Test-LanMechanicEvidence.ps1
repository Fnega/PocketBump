$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'LanDiagnosticEvidence.ps1')
# These invented reports test only validation logic. They are NOT gameplay, socket,
# natural-match, art, performance, or release-acceptance evidence.
$script:mechanicChecks = 0
$script:mechanicFailures = New-Object 'System.Collections.Generic.List[string]'
$mechanicRequirements = @{
    'stun-recovery' = @('stunned', 'stun_recovered')
    'green-impact' = @('green_pickup', 'green_enhanced_impact')
    'purple-immunity' = @('purple_pickup', 'purple_dynamic_immunity', 'purple_pillar_immunity')
    'blue-teleport' = @('blue_pickup', 'blue_safe_relocation')
    'blue-no-safe' = @('blue_pickup', 'blue_no_safe_destination')
    'fall-respawn' = @('fall_without_score', 'safe_respawn_without_score')
    'wall-player-repair' = @('dash_started', 'wall_broken', 'player_occupied_repair_pending', 'wall_restored_after_clearance')
    'wall-ball-repair' = @('dash_started', 'wall_broken', 'ball_occupied_repair_pending', 'ball_cleared_by_impact', 'wall_restored_after_clearance')
}
$mechanicEvents = @{
    stunned='Stunned'; green_pickup='Pickup'; green_enhanced_impact='Impact'; purple_pickup='Pickup'
    purple_dynamic_immunity='Impact'; purple_pillar_immunity='PillarHit'; blue_pickup='Pickup'; blue_safe_relocation='Teleport'
    blue_no_safe_destination='TeleportFailed'; fall_without_score='Fell'; safe_respawn_without_score='Respawned'
    dash_started='DashStarted'; wall_broken='WallBroken'; player_occupied_repair_pending='WallRepairPending'
    ball_occupied_repair_pending='WallRepairPending'; ball_cleared_by_impact='Impact'; wall_restored_after_clearance='WallRepaired'
}

function New-MechanicReportFixture([string]$Role, [string]$Case, [int]$Seat) {
    $localSeat = if ($Role -eq 'host') { '0' } else { '1' }
    $localEndpoint = if ($Role -eq 'host') { '127.0.0.1:21000' } else { '127.0.0.1:21001' }
    $remoteEndpoint = if ($Role -eq 'host') { '127.0.0.1:21001' } else { '127.0.0.1:21000' }
    $values = @{
        report_schema='dash_arena_v1'; protocol_version='7'; result='PASS'; whole_scenario_final='true'
        role=$Role; local_seat=$localSeat; scenario='mechanics'; fault_profile='none'; scope='same_machine_two_process'
        two_computer_validated='false'; action_vocabulary='movement,dash'; presentation_error_count='0'
        rules_hash=('1' * 64); layout_hash=('2' * 64); profile_id='mechanic_fixture'; seed_policy='1'; seed='43'
        send_attempts='100'; sent_datagrams='100'; received_datagrams='100'; send_failures='0'
        local_endpoint=$localEndpoint; remote_endpoint=$remoteEndpoint; first_socket_endpoint=$localEndpoint
        connection_id='12'; first_connection_id='12'; epoch='0'; terminal_reason=''; lifecycle_phase='Playing'
        stage='complete'; initialization_source='labeled_fixture'; finish_source='not_completed'; completed_epochs='0'
        mechanic_case=$Case; mechanic_seat=[string]$Seat; mechanic_pass='true'; mechanic_invalid_reason=''
        fixture_id=('dash_mechanic_v1_' + $Case + '_seat_' + $Seat); fixture_initial_checksum='1234567'
        fixture_prebroken_wall_id='-1'; required_milestones=([string]::Join(',', $mechanicRequirements[$Case]))
        missing_milestones=''; confirmed_state_tick='1000'; validated_checksums='150'
        milestone_count=[string]$mechanicRequirements[$Case].Count
        comparison_type='not_applicable'; green_actual_ball_speed='18'; green_unbuffed_counterfactual_ball_speed='12'
        green_comparison_tick='20'; green_comparison_input0='16'; green_comparison_input1='0'
    }
    if ($Case -eq 'green-impact') { $values.comparison_type = 'readonly_previous_confirmed_state_actual_inputs_remove_green_only' }
    if ($Case -eq 'wall-ball-repair') { $values.fixture_prebroken_wall_id = '41' }
    $eventIndex = 0
    for ($index = 0; $index -lt $mechanicRequirements[$Case].Count; $index++) {
        $name = $mechanicRequirements[$Case][$index]
        $tick = 10 + $index * 10; $entity = 2
        switch ($name) {
            'stun_recovered' { $tick = 46 }
            'safe_respawn_without_score' { $tick = 190 }
            'dash_started' { $entity = $Seat }
            'wall_broken' { $entity = 40; $tick = 20 }
            'player_occupied_repair_pending' { $entity = 40; $tick = 620 }
            'ball_occupied_repair_pending' { $entity = 41; $tick = 600 }
            'ball_cleared_by_impact' { $entity = 2; $tick = 630 }
            'wall_restored_after_clearance' { $entity = if ($Case -eq 'wall-ball-repair') { 41 } else { 40 }; $tick = 640 }
        }
        $prefix = "milestone.$index."
        $values[$prefix + 'name'] = $name; $values[$prefix + 'tick'] = [string]$tick
        $values[$prefix + 'epoch'] = '0'; $values[$prefix + 'seat'] = [string]$Seat
        $values[$prefix + 'entity'] = [string]$entity; $values[$prefix + 'rules_hash'] = $values.rules_hash
        $values[$prefix + 'outcome'] = 'synthetic validator outcome for ' + $name
        if ($mechanicEvents.ContainsKey($name)) {
            $eventSeat = if ($name -like '*repair_pending' -or $name -eq 'wall_restored_after_clearance') { '-1' } else { [string]$Seat }
            $eventPrefix = "event.$eventIndex."
            $values[$eventPrefix + 'connection_id']='12'; $values[$eventPrefix + 'epoch']='0'
            $values[$eventPrefix + 'tick']=[string]$tick; $values[$eventPrefix + 'seat']=$eventSeat
            $values[$eventPrefix + 'entity']=[string]$entity; $values[$eventPrefix + 'kind']=$mechanicEvents[$name]
            $values[$eventPrefix + 'ordinal']='0'; $values[$eventPrefix + 'rules_hash']=$values.rules_hash
            $values[$eventPrefix + 'x']='0'; $values[$eventPrefix + 'y']='0'
            $eventIndex++
        }
    }
    $values.confirmed_event_count = [string]$eventIndex
    return $values
}

function Assert-MechanicFixturePair($First, $Second, [string]$Case, [int]$Seat) {
    Assert-DashPairEvidence $First $Second 'mechanics' 'none' 'host' $false $false $Case $Seat
}
function Check-MechanicRejected([scriptblock]$Action, [string]$Label) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    $script:mechanicChecks++
    if (!$rejected) { $script:mechanicFailures.Add("Invalid synthetic mechanic evidence was accepted: $Label") }
}
function Check-MechanicMutation([string]$Case, [int]$Seat, [scriptblock]$Mutation, [string]$Label) {
    $first = New-MechanicReportFixture 'host' $Case $Seat
    $second = New-MechanicReportFixture 'join' $Case $Seat
    # Mutate BOTH peers so peer agreement alone cannot mask invalid evidence.
    & $Mutation $first $Case $Seat
    & $Mutation $second $Case $Seat
    Check-MechanicRejected { Assert-MechanicFixturePair $first $second $Case $Seat } "$Case/seat$Seat/$Label"
}
function Set-MilestoneFixtureTick($Values, [string]$Name, [int]$Tick) {
    for ($i = 0; $i -lt [int]$Values.milestone_count; $i++) {
        if ($Values["milestone.$i.name"] -cne $Name) { continue }
        $old = $Values["milestone.$i.tick"]
        $Values["milestone.$i.tick"] = [string]$Tick
        if ($mechanicEvents.ContainsKey($Name)) {
            for ($j = 0; $j -lt [int]$Values.confirmed_event_count; $j++) {
                if ($Values["event.$j.kind"] -ceq $mechanicEvents[$Name] -and $Values["event.$j.tick"] -eq $old) {
                    $Values["event.$j.tick"] = [string]$Tick
                }
            }
        }
    }
}

foreach ($case in $mechanicRequirements.Keys | Sort-Object) {
    foreach ($seat in @(0, 1)) {
        $first = New-MechanicReportFixture 'host' $case $seat
        $second = New-MechanicReportFixture 'join' $case $seat
        try { Assert-MechanicFixturePair $first $second $case $seat }
        catch { $script:mechanicFailures.Add("Valid synthetic fixture rejected: $case/seat$seat : $($_.Exception.Message)") }
        $script:mechanicChecks++
        Check-MechanicMutation $case $seat { param($v) $v.Remove('required_milestones') } 'missing required declaration'
        Check-MechanicMutation $case $seat { param($v) $v.required_milestones='one_fake_milestone' } 'wrong required declaration'
        Check-MechanicMutation $case $seat { param($v) $v.missing_milestones='not_recovered' } 'reported missing milestone'
        Check-MechanicMutation $case $seat { param($v) $v.mechanic_invalid_reason='observed_rule_violation' } 'nonempty observed invalid reason'
        Check-MechanicMutation $case $seat { param($v) $v.Remove('mechanic_invalid_reason') } 'missing invalid-reason field'
        Check-MechanicMutation $case $seat { param($v) $v['milestone.0.name']='invented_instead_of_required' } 'missing actual required milestone'
        Check-MechanicMutation $case $seat { param($v) $v['milestone.1.name']=$v['milestone.0.name'] } 'duplicate milestone identity'
        Check-MechanicMutation $case $seat { param($v,$c,$s) $v['milestone.0.seat']=[string](1-$s) } 'wrong milestone recipient'
        Check-MechanicMutation $case $seat { param($v) $v['milestone.0.epoch']='1' } 'wrong milestone epoch'
        Check-MechanicMutation $case $seat { param($v) $v['milestone.0.tick']='1001' } 'unconfirmed future milestone'
        Check-MechanicMutation $case $seat { param($v) $v['milestone.0.tick']='0.5' } 'fractional milestone tick'
        Check-MechanicMutation $case $seat { param($v) $v['milestone.0.tick']='NaN' } 'nonfinite milestone tick'
        Check-MechanicMutation $case $seat { param($v) $v['milestone.0.rules_hash']=('3'*64) } 'wrong milestone content hash'
        Check-MechanicMutation $case $seat { param($v) $v['milestone.0.outcome']='' } 'no actual outcome'
        Check-MechanicMutation $case $seat { param($v) $v.confirmed_event_count='0' } 'no corrected confirmed events'
        Check-MechanicMutation $case $seat { param($v) $v['event.0.kind']='FabricatedEvent' } 'required milestone lacks real event kind'
        Check-MechanicMutation $case $seat { param($v) $v['event.0.rules_hash']=('3'*64) } 'wrong matching-event content hash'
        Check-MechanicMutation $case $seat { param($v) $v['event.0.connection_id']='99' } 'event from another connection'
        Check-MechanicMutation $case $seat { param($v) $v['event.0.tick']='11' } 'event from another tick'
        Check-MechanicMutation $case $seat { param($v) $v.confirmed_state_tick='299' } 'insufficient confirmed progress'
        Check-MechanicMutation $case $seat { param($v) $v.validated_checksums='119' } 'insufficient actual checksum evidence'
        $different = $second.Clone(); $different['milestone.0.outcome']='different_peer_outcome'
        Check-MechanicRejected { Assert-MechanicFixturePair $first $different $case $seat } "$case/seat$seat/peer outcome divergence"
        $different = $second.Clone(); $different.fixture_initial_checksum='7654321'
        Check-MechanicRejected { Assert-MechanicFixturePair $first $different $case $seat } "$case/seat$seat/peer initial fixture divergence"
        switch ($case) {
            'stun-recovery' {
                Check-MechanicMutation $case $seat { param($v) Set-MilestoneFixtureTick $v 'stun_recovered' 45 } 'early recovery before 36 ticks'
            }
            'fall-respawn' {
                Check-MechanicMutation $case $seat { param($v) Set-MilestoneFixtureTick $v 'safe_respawn_without_score' 189 } 'early respawn before 180 ticks'
            }
            'green-impact' {
                Check-MechanicMutation $case $seat { param($v) $v.green_actual_ball_speed=$v.green_unbuffed_counterfactual_ball_speed } 'pickup alone does not prove stronger impact'
                Check-MechanicMutation $case $seat { param($v) $v['event.1.kind']='Pickup' } 'no actual Impact event'
                Check-MechanicMutation $case $seat { param($v) $v.comparison_type='invented_baseline' } 'unverified comparison origin'
                Check-MechanicMutation $case $seat { param($v) $v.green_comparison_tick='9' } 'comparison before actual pickup'
                Check-MechanicMutation $case $seat { param($v) $v.green_comparison_tick='15' } 'comparison after pickup but before enhanced Impact'
                Check-MechanicMutation $case $seat { param($v) $v.green_comparison_tick='21' } 'comparison after enhanced Impact'
                Check-MechanicMutation $case $seat {
                    param($v)
                    Set-MilestoneFixtureTick $v 'green_pickup' 0
                    Set-MilestoneFixtureTick $v 'green_enhanced_impact' 26
                    $v.green_comparison_tick='171'
                } 'synthetic regression of overwritten comparison Tick 171 versus first Impact Tick 26'
                # Reproduce the actual report's tick identities, but these remain
                # invented validator fixtures, not a replay or gameplay evidence.
                $sameFrameFirst = New-MechanicReportFixture 'host' $case $seat
                $sameFrameSecond = New-MechanicReportFixture 'join' $case $seat
                foreach ($sameFrame in @($sameFrameFirst, $sameFrameSecond)) {
                    Set-MilestoneFixtureTick $sameFrame 'green_pickup' 0
                    Set-MilestoneFixtureTick $sameFrame 'green_enhanced_impact' 26
                    $sameFrame.green_comparison_tick='26'
                }
                try { Assert-MechanicFixturePair $sameFrameFirst $sameFrameSecond $case $seat }
                catch { $script:mechanicFailures.Add("Valid same-Impact-tick synthetic fixture rejected: $case/seat$seat : $($_.Exception.Message)") }
                $script:mechanicChecks++
            }
            'purple-immunity' {
                Check-MechanicMutation $case $seat { param($v) $v['event.1.kind']='Pickup' } 'pickup does not prove dynamic immunity'
                Check-MechanicMutation $case $seat { param($v) $v['event.2.kind']='Pickup' } 'pickup does not prove pillar immunity'
            }
            'wall-player-repair' {
                Check-MechanicMutation $case $seat { param($v) Set-MilestoneFixtureTick $v 'player_occupied_repair_pending' 619 } 'repair before 600 advancing ticks'
                Check-MechanicMutation $case $seat { param($v) Set-MilestoneFixtureTick $v 'wall_restored_after_clearance' 620 } 'no later clearance interval'
                Check-MechanicMutation $case $seat { param($v) $v['event.1.kind']='WallHit' } 'no actual broken-wall event'
            }
            'wall-ball-repair' {
                Check-MechanicMutation $case $seat { param($v) Set-MilestoneFixtureTick $v 'ball_occupied_repair_pending' 598 } 'prebroken wall repaired before timer elapsed'
                Check-MechanicMutation $case $seat { param($v) Set-MilestoneFixtureTick $v 'ball_cleared_by_impact' 599 } 'no ball clearance after repair pending'
                Check-MechanicMutation $case $seat { param($v) $v['event.3.kind']='WallHit' } 'no actual ball-clearing Impact'
                Check-MechanicMutation $case $seat { param($v) $v.fixture_prebroken_wall_id='40' } 'prebroken wall identity misreported'
            }
        }
    }
}
if ($script:mechanicFailures.Count -gt 0) {
    foreach ($failure in $script:mechanicFailures) { Write-Warning $failure }
    throw "$($script:mechanicFailures.Count) failures among $script:mechanicChecks synthetic mechanic validator checks. No gameplay was run."
}
Write-Output "PASS: $script:mechanicChecks synthetic mechanic-report validator checks, including 8 cases x 2 seats. No gameplay, socket, natural-match, art, or release acceptance is claimed."
