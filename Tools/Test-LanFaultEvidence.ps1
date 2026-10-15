$ErrorActionPreference = 'Stop'
# Reuse the complete synthetic natural-game event fixture and run its historical
# protocol-7 regressions. These fixtures never launch Unity, a player or sockets.
$null = . (Join-Path $PSScriptRoot 'Test-LanDiagnosticEvidence.ps1')
$script:faultChecks = 0
function Assert-FaultRejected([scriptblock]$Action, [string]$Label) {
    $rejected = $false
    try { & $Action } catch { $rejected = $true }
    if (!$rejected) { throw "Invalid synthetic fault evidence accepted: $Label" }
    $script:faultChecks++
}
function New-DirectionalReportFixture([string]$Role, [string]$SilenceRole) {
    $values = New-ReportFixture $Role
    $silent = $Role -ceq $SilenceRole
    $values.protocol_version='8'; $values.scenario='fault-evidence'; $values.stage='fault-evidence-complete'
    $values.epoch='0'; $values.seed='42'; $values.finish_source='normal_gameplay'
    $values.finish_tick='2807'; $values.finish_checksum='54321'; $values.final_score_0='8'; $values.final_score_1='1'; $values.winner='0'
    $values.fault_evidence_scope='same_machine_two_process_confirmed_result_hold'
    $values.fault_injection_method='suppress_selected_process_outbound_after_confirmed_result'
    $values.fault_silence_role=$SilenceRole; $values.fault_required_sender=$SilenceRole
    $values.fault_silence_activated=if ($silent) { 'true' } else { 'false' }
    $values.local_fault_injection_required=$values.fault_silence_activated
    $values.injected_faults=if ($silent) { '60' } else { '0' }; $values.fault_injected_datagrams=$values.injected_faults
    $values.terminal_reason=if ($silent) { 'PEER FAULT' } else { 'PEER TIMEOUT' }
    $values.failure_origin=if ($silent) { 'peer' } else { 'local' }
    $values.peer_failure_reason=if ($silent) { 'PEER TIMEOUT' } else { '' }
    $values.fault_disconnect_seconds='5'; $values.fault_input_progress_timeout_seconds='5'
    $values.fault_timeline_retention_seconds='20'; $values.fault_frozen_hold_seconds='21.1'
    $values.fault_timeline_retained='true'; $values.fault_timeline_samples='5'
    $values.fault_timeline_sha256='A' * 64; $values.fault_first_report_sha256='B' * 64; $values.fault_retained_report_sha256='C' * 64
    $values.fault_first_report='synthetic first file'; $values.fault_retained_report='synthetic retained file'
    return $values
}
function Assert-DirectionalFixture($First, $Second, [string]$SilenceRole) {
    Assert-DashPairEvidence $First $Second 'fault-evidence' 'none' 'host' $false $false '' 0 'host' $SilenceRole 8
}
$currentHost = New-ReportFixture 'host'; $currentJoin = New-ReportFixture 'join'
$currentHost.protocol_version='8'; $currentJoin.protocol_version='8'
Assert-DashPairEvidence $currentHost $currentJoin 'full-match' 'none' 'host' $false $false '' 0 'host' 'host' 8
$script:faultChecks++
Assert-FaultRejected { Assert-DashPairEvidence $currentHost $currentJoin 'full-match' 'none' 'host' $false $false '' 0 'host' 'host' 7 } 'current reports cannot stand in for another build protocol'
foreach ($peer in @($currentHost,$currentJoin)) {
    $peer.expects_version_mismatch='true'; $peer.terminal_reason='SETTINGS MISMATCH'; $peer.simulation_tick='0'; $peer.completed_epochs='0'
}
$currentHost.advertised_protocol_version='8'; $currentJoin.advertised_protocol_version='7'
Assert-DashPairEvidence $currentHost $currentJoin 'full-match' 'none' 'host' $true $false '' 0 'host' 'host' 8
$script:faultChecks++
foreach ($silenceRole in @('host', 'join')) {
    $first = New-DirectionalReportFixture 'host' $silenceRole
    $second = New-DirectionalReportFixture 'join' $silenceRole
    Assert-DirectionalFixture $first $second $silenceRole
    $script:faultChecks++
    foreach ($mutation in @(
        @('fault_evidence_scope','live_interruption'), @('fault_injection_method','pretend_silence'),
        @('fault_silence_role','neither'), @('fault_frozen_hold_seconds','19.9'), @('fault_frozen_hold_seconds','NaN'),
        @('fault_timeline_retained','false'), @('fault_timeline_samples','0'), @('fault_timeline_samples','82'),
        @('fault_disconnect_seconds','6'), @('fault_input_progress_timeout_seconds','6'),
        @('fault_timeline_retention_seconds','60'), @('fault_timeline_sha256',''), @('fault_first_report_sha256','changed'),
        @('fault_retained_report',''), @('stage','results-hold'), @('completed_epochs','0'),
        @('initialization_source','labeled_fixture'), @('finish_source','not_completed'),
        @('finish_checksum','111'), @('protocol_version','7'), @('presentation_error_count','1'),
        @('event.2.kind','Pickup'), @('event.36.tick','2806')
    )) {
        $badFirst = $first.Clone(); $badSecond = $second.Clone()
        $badFirst[$mutation[0]]=$mutation[1]; $badSecond[$mutation[0]]=$mutation[1]
        Assert-FaultRejected { Assert-DirectionalFixture $badFirst $badSecond $silenceRole } ('matching corruption ' + ($mutation -join '='))
    }
    foreach ($role in @('host', 'join')) {
        foreach ($mutation in @(
            @('terminal_reason','PEER LEFT'), @('failure_origin','none'), @('peer_failure_reason','DESYNC'),
            @('fault_injected_datagrams','-1'), @('injected_faults','99'), @('fault_required_sender','both'),
            @('epoch.0.checksum','54320')
        )) {
            $badFirst = $first.Clone(); $badSecond = $second.Clone()
            $target = if ($role -eq 'host') { $badFirst } else { $badSecond }
            $target[$mutation[0]]=$mutation[1]
            Assert-FaultRejected { Assert-DirectionalFixture $badFirst $badSecond $silenceRole } ($role + ' corruption ' + ($mutation -join '='))
        }
    }
    Assert-FaultRejected { Assert-DashPairEvidence $first $second 'fault-evidence' 'none' 'host' $false $true '' 0 'host' $silenceRole 8 } 'combined loss profile'
    Assert-FaultRejected { Assert-DashPairEvidence $first $second 'fault-evidence' 'none' 'host' $true $false '' 0 'host' $silenceRole 8 } 'combined mismatch profile'
}

# The launcher must inspect the retained JSON bytes too, not just PASS strings.
$fixtureParent = Join-Path $PSScriptRoot 'LanDiagnosticResults'
$fixtureRoot = Join-Path $fixtureParent ('FaultEvidenceValidatorFixture-' + [Guid]::NewGuid().ToString('N'))
$null = [IO.Directory]::CreateDirectory($fixtureRoot)
$diagnostic = Join-Path $fixtureRoot 'host.txt'
$values = New-DirectionalReportFixture 'host' 'join'
$buildGuid = [Guid]::NewGuid().ToString('N')
$samples = @()
for ($i=0; $i -lt 5; $i++) {
    $samples += [pscustomobject]@{
        elapsed_seconds=[single]($i*1.25); failure_reason=$(if($i -eq 4){'PEER TIMEOUT'}else{''});
        failure_origin=$(if($i -eq 4){'local'}else{'none'}); peer_failure_reason=''; receive_polls=100+$i;
        accepted_peer_age_seconds=[single]($i*1.25)
    }
}
$snapshot = [pscustomobject]@{
    schema='pocketbump_lan_release_v1'; evidence_version=2; development_build=$true; protocol_version=8; timeline_frozen=$true;
    build_guid=$buildGuid; match_id=12; epoch=0; role='host'; failure_reason='PEER TIMEOUT'; failure_origin='local'; peer_failure_reason='';
    rules_hash=('1'*64); layout_hash=('2'*64); timeline=$samples; report_id='synthetic-retained-scope'; simulation_tick=2808; checksum_tick=2807; checksum=54321;
    process_id=12345; write_sequence=0; event_name=''; captured_utc=''
}
$snapshotJson = @{}
foreach ($kind in @('first','retained')) {
    $path=$diagnostic + '.fault-' + $kind + '.json'
    $snapshot.event_name='development_fault-' + $kind
    $snapshot.write_sequence=if ($kind -eq 'first') { 8 } else { 13 }
    $snapshot.captured_utc=if ($kind -eq 'first') { '2026-10-14T00:00:00.0000000Z' } else { '2026-10-14T00:00:21.1000000Z' }
    $snapshotJson[$kind]=$snapshot|ConvertTo-Json -Depth 8
    [IO.File]::WriteAllText($path, $snapshotJson[$kind], [Text.UTF8Encoding]::new($false))
    $values['fault_' + $kind + '_report']=$path
    $values['fault_' + $kind + '_report_sha256']=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
Assert-DashFaultReportFiles $values $diagnostic $buildGuid
$script:faultChecks++
Assert-FaultRejected { Assert-DashFaultReportFiles $values $diagnostic ([Guid]::NewGuid().ToString('N')) } 'wrong actual candidate GUID'
$bad=$values.Clone(); $bad.fault_first_report=$values.fault_retained_report
Assert-FaultRejected { Assert-DashFaultReportFiles $bad $diagnostic $buildGuid } 'redirected retained evidence'
$bad=$values.Clone(); $bad.fault_retained_report_sha256='0'*64
Assert-FaultRejected { Assert-DashFaultReportFiles $bad $diagnostic $buildGuid } 'changed retained bytes'
function Write-RetainedFaultFixture([string]$Json) {
    [IO.File]::WriteAllText($values.fault_retained_report, $Json, [Text.UTF8Encoding]::new($false))
    $values.fault_retained_report_sha256=(Get-FileHash -LiteralPath $values.fault_retained_report -Algorithm SHA256).Hash
}
Write-RetainedFaultFixture $snapshotJson.first
Assert-FaultRejected { Assert-DashFaultReportFiles $values $diagnostic $buildGuid } 'exact copied first snapshot cannot prove retention even with matching file hash'
foreach ($mutation in @(
    @('event_name', 'development_fault-first'), @('event_name', ''), @('process_id', 23456), @('process_id', 0),
    @('write_sequence', 8), @('write_sequence', 7), @('write_sequence', 0),
    @('captured_utc', '2026-10-14T00:00:00.0000000Z'), @('captured_utc', '2026-10-14T00:00:20.8990000Z'),
    @('captured_utc', '2026-10-13T23:59:59.0000000Z'), @('captured_utc', 'invalid'),
    @('simulation_tick', 2807), @('checksum_tick', 2806), @('checksum', 54320)
)) {
    $altered=$snapshotJson.retained|ConvertFrom-Json
    $altered.($mutation[0])=$mutation[1]
    Write-RetainedFaultFixture ($altered|ConvertTo-Json -Depth 8)
    Assert-FaultRejected { Assert-DashFaultReportFiles $values $diagnostic $buildGuid } ('retained generation/state corruption ' + ($mutation -join '='))
}
# Two mutually consistent but unrelated frozen states must not replace the real finish.
foreach ($kind in @('first','retained')) {
    $altered=$snapshotJson[$kind]|ConvertFrom-Json
    $altered.simulation_tick=2809; $altered.checksum_tick=2808; $altered.checksum=111
    $path=$values['fault_' + $kind + '_report']
    [IO.File]::WriteAllText($path, ($altered|ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
    $values['fault_' + $kind + '_report_sha256']=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash
}
Assert-FaultRejected { Assert-DashFaultReportFiles $values $diagnostic $buildGuid } 'identical wrong finish states cannot pass by pair equality'
[IO.File]::WriteAllText($values.fault_first_report, $snapshotJson.first, [Text.UTF8Encoding]::new($false))
$values.fault_first_report_sha256=(Get-FileHash -LiteralPath $values.fault_first_report -Algorithm SHA256).Hash
Write-RetainedFaultFixture $snapshotJson.retained
Assert-DashFaultReportFiles $values $diagnostic $buildGuid
$script:faultChecks++
$snapshot.timeline[0].receive_polls=55
[IO.File]::WriteAllText($values.fault_retained_report, ($snapshot|ConvertTo-Json -Depth 8), [Text.UTF8Encoding]::new($false))
$values.fault_retained_report_sha256=(Get-FileHash -LiteralPath $values.fault_retained_report -Algorithm SHA256).Hash
Assert-FaultRejected { Assert-DashFaultReportFiles $values $diagnostic $buildGuid } 'timeline changed even when new file hash is supplied'
Write-Host "Fault-evidence synthetic validator checks passed: $script:faultChecks; fixture=$fixtureRoot"
