param([Parameter(Mandatory=$true)][string]$PlayerPath)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $PlayerPath -PathType Leaf)) { throw "Player not found: $PlayerPath" }
$PlayerPath = (Resolve-Path -LiteralPath $PlayerPath).Path
$playerDirectory = Split-Path -Parent $PlayerPath
$metadataPath = Join-Path $playerDirectory 'build-info.json'
$metadata = Get-Content -LiteralPath $metadataPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($metadata.development -ne $true) { throw 'This suite requires the separately identified Development companion, not the Release player.' }
$runtimePath = Join-Path $playerDirectory (([IO.Path]::GetFileNameWithoutExtension($PlayerPath)) + '_Data\Managed\PocketBump.Runtime.dll')
$runId = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$runRoot = Join-Path $PSScriptRoot ('DemoDevelopmentResults\' + $runId)
$null = New-Item -ItemType Directory -Path $runRoot
$launcher = Join-Path $PSScriptRoot 'Run-LanDiagnostics.ps1'
$cases = @(
    @{ name='pacing-host-stall'; args=@('-Scenario','pacing','-PacingStallRole','host','-ScenarioTimeoutSeconds','90') },
    @{ name='pacing-join-stall'; args=@('-Scenario','pacing','-PacingStallRole','join','-ScenarioTimeoutSeconds','90') },
    @{ name='fault-host-outbound-silence'; args=@('-Scenario','fault-evidence','-SilenceRole','host','-ScenarioTimeoutSeconds','240') },
    @{ name='fault-join-outbound-silence'; args=@('-Scenario','fault-evidence','-SilenceRole','join','-ScenarioTimeoutSeconds','240') },
    @{ name='natural-full-match'; args=@('-Scenario','full-match','-ScenarioTimeoutSeconds','240') },
    @{ name='three-rematches-host-leaves'; args=@('-Scenario','lifecycle','-LeaveRole','host','-ScenarioTimeoutSeconds','480') },
    @{ name='three-rematches-join-leaves'; args=@('-Scenario','lifecycle','-LeaveRole','join','-ScenarioTimeoutSeconds','480') }
)
$records = @()
$failed = $false
$runtimeHash = (Get-FileHash -LiteralPath $runtimePath -Algorithm SHA256).Hash
$metadataHash = (Get-FileHash -LiteralPath $metadataPath -Algorithm SHA256).Hash
function Read-DemoDevelopmentReport([string]$Path) {
    $values = @{}
    foreach ($line in (Get-Content -LiteralPath $Path)) {
        if ($line -match '^([^=]+)=(.*)$') { $values[$matches[1]] = $matches[2] }
    }
    return $values
}
Write-Host "Development companion evidence: $runRoot"
Write-Host 'Scope: same-machine Development diagnostics; not Release UI or physical two-computer evidence.'
foreach ($case in $cases) {
    Write-Host "[$($records.Count+1)/$($cases.Count)] $($case.name)"
    $started = [DateTime]::UtcNow
    $arguments = @('-NoProfile','-ExecutionPolicy','Bypass','-File',$launcher,'-PlayerPath',$PlayerPath) + $case.args
    $output = @(& powershell.exe @arguments 2>&1)
    $exitCode = $LASTEXITCODE
    $lines = @($output | ForEach-Object { [string]$_ })
    $caseLog = Join-Path $runRoot ($case.name + '-launcher.log')
    [IO.File]::WriteAllLines($caseLog, $lines, [Text.UTF8Encoding]::new($false))
    $resultLine = $lines | Where-Object { $_ -match '^LAN diagnostic results: (.+)$' } | Select-Object -Last 1
    $resultDirectory = if ($null -ne $resultLine) { $resultLine.Substring('LAN diagnostic results: '.Length) } else { '' }
    $casePassed = $exitCode -eq 0 -and $resultDirectory.Length -gt 0
    $peers = @()
    $errors = @()
    if ($resultDirectory.Length -gt 0) {
        foreach ($role in @('host','join')) {
            $path = Join-Path $resultDirectory ($role + '.txt')
            if (!(Test-Path -LiteralPath $path -PathType Leaf)) { $casePassed=$false; $errors += "Missing $role report"; continue }
            $report = Read-DemoDevelopmentReport $path
            if ($report['result'] -ne 'PASS') { $casePassed=$false; $errors += "$role report failed: $($report['summary'])" }
            $epochs = @()
            for ($epoch=0; $epoch -lt [int]$report['completed_epochs']; $epoch++) {
                $prefix = "epoch.$epoch."
                $epochs += [pscustomobject]@{
                    epoch=$epoch; finish_source=$report[$prefix+'finish_source']; finish_tick=$report[$prefix+'finish_tick'];
                    checksum=$report[$prefix+'checksum']; score0=$report[$prefix+'score0']; score1=$report[$prefix+'score1'];
                    winner=$report[$prefix+'winner']; collision_goals0=$report[$prefix+'collision_goals0'];
                    collision_goals1=$report[$prefix+'collision_goals1']; hold_seconds=$report[$prefix+'hold_seconds']
                }
            }
            $peers += [pscustomobject]@{
                role=$role; result=$report['result']; summary=$report['summary']; terminal_reason=$report['terminal_reason'];
                failure_origin=$report['failure_origin']; peer_failure_reason=$report['peer_failure_reason'];
                fault_evidence_scope=$report['fault_evidence_scope']; fault_silence_role=$report['fault_silence_role'];
                fault_injected_datagrams=$report['fault_injected_datagrams']; fault_frozen_hold_seconds=$report['fault_frozen_hold_seconds'];
                fault_timeline_retained=$report['fault_timeline_retained']; fault_timeline_samples=$report['fault_timeline_samples'];
                fault_first_report=$report['fault_first_report']; fault_retained_report=$report['fault_retained_report'];
                successful_rematches=$report['successful_rematches']; observed_departure=$report['observed_departure'];
                send_failures=$report['send_failures']; presentation_error_count=$report['presentation_error_count'];
                validated_checksums=$report['validated_checksums']; completed_epochs=$epochs;
                pacing_stall_count=$report['pacing_stall_count']; pacing_actual_stall_seconds=$report['pacing_actual_stall_seconds'];
                pacing_wait_observed=$report['pacing_wait_observed']; pacing_resume_observed=$report['pacing_resume_observed'];
                pacing_recovery_confirmed_ticks=$report['pacing_recovery_confirmed_ticks'];
                pacing_checkpoint_tick=$report['pacing_checkpoint_tick']; pacing_checkpoint_checksum=$report['pacing_checkpoint_checksum']
            }
            $logPath = Join-Path $resultDirectory ($role+'-player.log')
            $logErrors = @(Select-String -LiteralPath $logPath -Pattern '^.*(?:Exception:|Assertion failed|Crash!!!)' -CaseSensitive:$false)
            foreach ($errorLine in $logErrors) { $casePassed=$false; $errors += "$role log $($errorLine.LineNumber): $($errorLine.Line)" }
        }
    }
    if (!$casePassed) { $failed=$true }
    $records += [pscustomobject]@{
        name=$case.name; passed=$casePassed; exit_code=$exitCode; arguments=$case.args;
        started_utc=$started.ToString('O'); finished_utc=[DateTime]::UtcNow.ToString('O');
        result_directory=$resultDirectory; launcher_log=$caseLog; peers=$peers; errors=$errors
    }
    $index = [ordered]@{
        schema='pocketbump_demo_development_regression_v1'; player=$PlayerPath; build_guid=$metadata.build_guid;
        development=$true; runtime_assembly_sha256=$runtimeHash; build_info_sha256=$metadataHash;
        scope='same_machine_two_process_development'; two_computer_validated=$false;
        required_cases=$cases.Count; completed_cases=$records.Count; passed=(!$failed -and $records.Count -eq $cases.Count); records=$records
    }
    [IO.File]::WriteAllText((Join-Path $runRoot 'acceptance-index.json'), ($index | ConvertTo-Json -Depth 10), [Text.UTF8Encoding]::new($false))
    Write-Host "[$($records.Count)/$($cases.Count)] $($case.name): $(if($casePassed){'PASS'}else{'FAIL'}); $resultDirectory"
    if (!$casePassed) { break }
}
Write-Host "Retained Development index: $(Join-Path $runRoot 'acceptance-index.json')"
if ($failed -or $records.Count -ne $cases.Count) { exit 1 }
exit 0
