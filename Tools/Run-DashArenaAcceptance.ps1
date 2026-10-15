param(
    [Parameter(Mandatory = $true)] [string]$PlayerPath,
    [ValidateSet('Smoke', 'Full')] [string]$Suite = 'Smoke',
    [switch]$KeepGoingAfterFailure
)
$ErrorActionPreference = 'Stop'
if (!(Test-Path -LiteralPath $PlayerPath -PathType Leaf)) { throw "Player not found: $PlayerPath" }
$PlayerPath = (Resolve-Path -LiteralPath $PlayerPath).Path
$runId = (Get-Date -Format 'yyyyMMdd-HHmmss-fff') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 6)
$runRoot = Join-Path $PSScriptRoot ('DashArenaAcceptanceResults\' + $runId)
New-Item -ItemType Directory -Path $runRoot | Out-Null
$launcher = Join-Path $PSScriptRoot 'Run-LanDiagnostics.ps1'
$cases = @(
    @{ Name='movement-smoke'; Arguments=@() },
    @{ Name='handshake-first-loss'; Arguments=@('-DropFirstControlPacket') },
    @{ Name='version-mismatch'; Arguments=@('-ExpectVersionMismatch') }
)
if ($Suite -eq 'Full') {
    $cases += @(
        @{ Name='pacing-host-stall'; Arguments=@('-Scenario', 'pacing', '-PacingStallRole', 'host') },
        @{ Name='pacing-join-stall'; Arguments=@('-Scenario', 'pacing', '-PacingStallRole', 'join') },
        @{ Name='natural-full-match'; Arguments=@('-Scenario', 'full-match') },
        @{ Name='three-rematches-host-leaves'; Arguments=@('-Scenario', 'lifecycle', '-LeaveRole', 'host') },
        @{ Name='three-rematches-join-leaves'; Arguments=@('-Scenario', 'lifecycle', '-LeaveRole', 'join') }
    )
    foreach ($fault in @('drop-finish', 'drop-finish-ack', 'drop-consent', 'drop-consent-ack', 'drop-offer',
            'drop-prepared', 'drop-commit', 'drop-commit-ack', 'drop-start', 'drop-startup-input', 'drop-startup-ack',
            'duplicate-reorder', 'stale-epoch', 'peer-silence')) {
        $cases += @{ Name=('fault-' + $fault); Arguments=@('-Scenario', 'full-match', '-FaultProfile', $fault) }
    }
    foreach ($mechanic in @('stun-recovery', 'green-impact', 'purple-immunity', 'blue-teleport', 'blue-no-safe',
            'fall-respawn', 'wall-player-repair', 'wall-ball-repair')) {
        foreach ($seat in @(0, 1)) {
            $cases += @{ Name=("mechanic-$mechanic-seat-$seat"); Arguments=@('-Scenario', 'mechanics', '-Mechanic', $mechanic, '-MechanicSeat', [string]$seat) }
        }
    }
}
$records = @()
$failed = $false
Write-Host "Acceptance results: $runRoot"
Write-Host 'Scope: same-machine two-process UDP only; NOT physical two-computer/Wi-Fi validation.'
foreach ($caseDefinition in $cases) {
    $caseStarted = Get-Date
    Write-Host "[$($records.Count + 1)/$($cases.Count)] Starting $($caseDefinition.Name)"
    $arguments = @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', $launcher, '-PlayerPath', $PlayerPath) + $caseDefinition.Arguments
    # Separate PowerShell process: the child launcher owns and cleans up only its players.
    $output = @(& powershell.exe @arguments 2>&1)
    $caseExit = $LASTEXITCODE
    $lines = @($output | ForEach-Object { [string]$_ })
    $caseLog = Join-Path $runRoot ($caseDefinition.Name + '-launcher.log')
    [System.IO.File]::WriteAllLines($caseLog, $lines, [System.Text.UTF8Encoding]::new($false))
    $resultLine = $lines | Where-Object { $_ -match '^LAN diagnostic results: (.+)$' } | Select-Object -Last 1
    $evidenceDirectory = if ($null -ne $resultLine) { $resultLine.Substring('LAN diagnostic results: '.Length) } else { '' }
    $passed = $caseExit -eq 0 -and $evidenceDirectory.Length -gt 0
    if (!$passed) { $failed = $true }
    $records += [pscustomobject]@{
        name=$caseDefinition.Name; passed=$passed; exit_code=$caseExit; arguments=$caseDefinition.Arguments;
        started=$caseStarted.ToString('o'); finished=(Get-Date).ToString('o');
        result_directory=$evidenceDirectory; launcher_log=$caseLog
    }
    $index = [ordered]@{
        player=$PlayerPath; suite=$Suite; scope='same_machine_two_process'; two_computer_validated=$false;
        required_cases=$cases.Count; completed_cases=$records.Count;
        passed=(!$failed -and $records.Count -eq $cases.Count); records=$records
    }
    [System.IO.File]::WriteAllText((Join-Path $runRoot 'acceptance-index.json'), ($index | ConvertTo-Json -Depth 7), [System.Text.UTF8Encoding]::new($false))
    Write-Host "[$($records.Count)/$($cases.Count)] $($caseDefinition.Name): $(if ($passed) { 'PASS' } else { 'FAIL' }); $evidenceDirectory"
    if (!$passed -and !$KeepGoingAfterFailure) { break }
}
Write-Host "Retained acceptance index: $(Join-Path $runRoot 'acceptance-index.json')"
if ($failed -or $records.Count -ne $cases.Count) { exit 1 }
exit 0
