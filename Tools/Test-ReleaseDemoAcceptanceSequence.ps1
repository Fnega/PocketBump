$ErrorActionPreference = 'Stop'
# Extract only the four sequencing functions. Never dot-source the executable
# acceptance script or load desktop APIs; all UI/report operations are mocks.
$driver = Join-Path $PSScriptRoot 'Run-ReleaseDemoAcceptance.ps1'
$tokens = $null; $parseErrors = $null
$ast = [Management.Automation.Language.Parser]::ParseFile($driver, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -ne 0) { throw 'Acceptance driver has parse errors.' }
foreach ($name in @('Start-HostUi', 'Prepare-JoinUi', 'Confirm-PreparedJoinUi', 'Start-ReleaseLanPairUi')) {
    $functions = @($ast.FindAll({ param($node)
        $node -is [Management.Automation.Language.FunctionDefinitionAst] -and $node.Name -eq $name
    }, $true))
    if ($functions.Count -ne 1) { throw "Expected exactly one function: $name" }
    . ([scriptblock]::Create($functions[0].Extent.Text))
}
$script:checksPassed = 0
$script:events = [Collections.Generic.List[string]]::new()
$connectionTimings = [Collections.Generic.List[object]]::new()
$script:rejectBound = $false
function Assert-Sequence([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw "Sequence fixture failed: $Label" }
    $script:checksPassed++
}
function Click-ReleaseDemoUi($Ticket, $X, $Y, $OffsetX, $OffsetY) {
    $script:events.Add("click:$($Ticket.process_id):${OffsetX}:${OffsetY}")
}
function Set-ReleaseDemoAddress($Ticket) { $script:events.Add("address:$($Ticket.process_id)") }
function Wait-ReleaseReport($Ticket, [scriptblock]$Predicate, [string]$Description) {
    $script:events.Add("bound:$($Ticket.process_id)")
    if ($script:rejectBound) { throw 'Synthetic host not bound.' }
    $healthy = [pscustomobject]@{ role='host'; socket_bound=$true; failure_reason='';
        session_started_utc=[DateTime]::UtcNow.AddSeconds(1).ToString('O'); report_id='new-host-session' }
    Assert-Sequence (& $Predicate $healthy) 'new healthy host is accepted'
    $healthy.session_started_utc = [DateTime]::UtcNow.AddHours(-1).ToString('O')
    Assert-Sequence (!(& $Predicate $healthy)) 'older session from same process is rejected'
    $healthy.session_started_utc = [DateTime]::UtcNow.AddSeconds(1).ToString('O')
    $healthy.failure_reason = 'LAN HANDSHAKE TIMEOUT'
    Assert-Sequence (!(& $Predicate $healthy)) 'faulted bound host is rejected'
    $healthy.failure_reason = ''; $healthy.socket_bound = $false
    Assert-Sequence (!(& $Predicate $healthy)) 'closed socket is rejected'
    $healthy.socket_bound = $true; $healthy.role = 'join'
    Assert-Sequence (!(& $Predicate $healthy)) 'old join role is rejected'
    $healthy.role = 'host'
    return [pscustomobject]@{data=$healthy}
}
$first = [pscustomobject]@{process_id=123}
$second = [pscustomobject]@{process_id=456}
foreach ($reverse in @($false, $true)) {
    $script:events.Clear()
    $pairHost = if ($reverse) { $second } else { $first }
    $pairJoin = if ($reverse) { $first } else { $second }
    $label = if ($reverse) { 'reverse fixture' } else { 'first fixture' }
    Start-ReleaseLanPairUi $pairHost $pairJoin $label
    $expected = @("click:$($pairJoin.process_id):0:-24", "address:$($pairJoin.process_id)",
        "click:$($pairHost.process_id):0:52", "click:$($pairHost.process_id):140.4:-302.4",
        "bound:$($pairHost.process_id)", "click:$($pairJoin.process_id):115.2:-122.4")
    Assert-Sequence (($script:events -join '|') -ceq ($expected -join '|')) "$label prefill precedes host, join follows fresh bound"
    $timing = $connectionTimings[$connectionTimings.Count - 1]
    Assert-Sequence ($timing.host_report_id -ceq 'new-host-session') "$label records observed report"
    Assert-Sequence ($timing.host_process_id -eq $pairHost.process_id -and $timing.join_process_id -eq $pairJoin.process_id) "$label records exact roles"
    Assert-Sequence ([DateTime]::Parse($timing.host_ui_started_utc) -le [DateTime]::Parse($timing.host_bound_observed_utc) -and
        [DateTime]::Parse($timing.host_bound_observed_utc) -le [DateTime]::Parse($timing.join_confirmed_utc)) "$label records ordered timestamps"
}
$script:events.Clear(); $script:rejectBound = $true; $rejected = $false
try { Start-ReleaseLanPairUi $first $second 'failed host fixture' } catch { $rejected = $_.Exception.Message -eq 'Synthetic host not bound.' }
Assert-Sequence $rejected 'host observation failure propagates'
Assert-Sequence ($script:events.Count -eq 5 -and $script:events[4] -ceq 'bound:123') 'join confirmation is never sent after host failure'
Assert-Sequence ($connectionTimings[2].join_confirmed_utc -ceq '') 'incomplete timing is not presented as joined'
Assert-Sequence ($connectionTimings[2].host_ui_started_utc.Length -gt 0) 'failed attempt retains its start timing'
Write-Output "Release acceptance sequence fixtures passed: $script:checksPassed. No desktop APIs or games were invoked."
