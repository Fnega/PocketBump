# One-time, explicitly approved 1.0 delivery setup. Never changes existing rules,
# network categories, public/domain profiles, or firewall enablement.
[CmdletBinding(SupportsShouldProcess=$true)]
param()
$ErrorActionPreference = 'Stop'
$candidate = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../Builds/Demo1.0-20261014-candidate3/PocketBump.exe'))
$gameDirectory = -join [char[]](0x5c04,0x7403,0x5927,0x6218)
$delivery = 'D:\uniry Assets\' + $gameDirectory + '\PocketBump1.0\PocketBump.exe'
$plans = @(
    [pscustomobject]@{name='PocketBump1.0-Private-UDP7777-Candidate3';program=$candidate},
    [pscustomobject]@{name='PocketBump1.0-Private-UDP7777-Release';program=$delivery}
)
$build = Get-Content -LiteralPath (Join-Path (Split-Path $candidate) 'build-info.json') -Raw -Encoding UTF8 | ConvertFrom-Json
if ($build.build_guid -cne 'b224be1f86f740199b1db86c11ac4ef0' -or $build.development -ne $false) { throw 'Unexpected candidate identity.' }
$version = [Diagnostics.FileVersionInfo]::GetVersionInfo($candidate)
if ($version.ProductName -cne 'PocketBump' -or $version.CompanyName -cne 'Fnega' -or $version.ProductVersion -cne '1.0') { throw 'Unexpected candidate product.' }

function Read-ScopedRule($Rule) {
    $application = $Rule | Get-NetFirewallApplicationFilter
    $port = $Rule | Get-NetFirewallPortFilter
    $address = $Rule | Get-NetFirewallAddressFilter
    [pscustomobject]@{
        name=$Rule.Name; program=$application.Program; profile=[string]$Rule.Profile
        enabled=[string]$Rule.Enabled; direction=[string]$Rule.Direction; action=[string]$Rule.Action
        protocol=[string]$port.Protocol; local_port=@($port.LocalPort)
        remote_address=@($address.RemoteAddress); edge_traversal=[string]$Rule.EdgeTraversalPolicy
    }
}
function Assert-ExactRule($Rule, $Plan) {
    $actual = Read-ScopedRule $Rule
    $remote = @($actual.remote_address | Sort-Object)
    if ($actual.program -ine $Plan.program -or $actual.profile -cne 'Private' -or
        $actual.enabled -cne 'True' -or $actual.direction -cne 'Inbound' -or $actual.action -cne 'Allow' -or
        $actual.protocol -notin @('UDP','17') -or ($actual.local_port -join ',') -cne '7777' -or
        ($remote -join ',') -ine '127.0.0.1,LocalSubnet' -or $actual.edge_traversal -cne 'Block') {
        throw ('Existing/resulting rule differs from the approved narrow scope: ' + $Plan.name)
    }
    return $actual
}
$result = [ordered]@{
    schema='pocketbump_private_firewall_setup_v1'; started_utc=[DateTime]::UtcNow.ToString('o')
    passed=$false; error=''; created=@(); verified=@(); existing_program_rules=@()
    changed_network_category=$false; changed_existing_rules=$false; changed_firewall_enablement=$false
}
if ($WhatIfPreference) { $plans | Format-Table; Write-Output 'Private only; Inbound UDP 7777; LocalSubnet and loopback only; no existing-rule changes.'; return }
$principal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Administrator confirmation is required; no rules changed.' }
$reportDirectory = Join-Path $PSScriptRoot 'ReleaseResults/20261014-1.0'
$reportPath = Join-Path $reportDirectory ('firewall-private-' + [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0,8) + '.json')
try {
    $existingRules = @(Get-NetFirewallRule -PolicyStore PersistentStore)
    foreach ($plan in $plans) {
        $sameName = @($existingRules | Where-Object { $_.Name -ceq $plan.name })
        if ($sameName.Count -gt 1) { throw 'Duplicate rule name.' }
        if ($sameName.Count -eq 1) { $null = Assert-ExactRule $sameName[0] $plan }
    }
    foreach ($filter in @(Get-NetFirewallApplicationFilter -PolicyStore PersistentStore | Where-Object { @($candidate,$delivery) -contains $_.Program })) {
        foreach ($rule in @($filter | Get-NetFirewallRule)) { $result.existing_program_rules += Read-ScopedRule $rule }
    }
    foreach ($plan in $plans) {
        $rule = @($existingRules | Where-Object { $_.Name -ceq $plan.name })
        if ($rule.Count -eq 0 -and $PSCmdlet.ShouldProcess($plan.program,'Add Private-only inbound UDP 7777 from local subnet/loopback')) {
            $rule = @(New-NetFirewallRule -PolicyStore PersistentStore -Name $plan.name -DisplayName $plan.name `
                -Description 'User-approved PocketBump 1.0 LAN: Private profile only, UDP 7777, local subnet or loopback.' `
                -Program $plan.program -Direction Inbound -Action Allow -Enabled True -Profile Private `
                -Protocol UDP -LocalPort 7777 -RemoteAddress LocalSubnet,127.0.0.1 -EdgeTraversalPolicy Block)
            $result.created += $plan.name
        }
        if ($rule.Count -ne 1) { throw 'Approved rule was not created.' }
        $result.verified += Assert-ExactRule $rule[0] $plan
    }
    $result.passed = $true
}
catch { $result.error = $_.Exception.Message; throw }
finally {
    $result.completed_utc = [DateTime]::UtcNow.ToString('o')
    $result | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $reportPath -Encoding UTF8
    Write-Output ('Firewall setup report: ' + $reportPath)
}
