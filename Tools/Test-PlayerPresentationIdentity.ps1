$ErrorActionPreference = 'Stop'
# Synthetic identity observations and virtual time only. No process lookup,
# process launch/termination, window enumeration, sleep, or desktop API calls.
. (Join-Path $PSScriptRoot 'PlayerPresentationWindows.ps1')
$script:identityChecks = 0
$expected = [pscustomobject]@{
    process_id=1234; start_time_utc_ticks=638000000000000000L
    executable=[IO.Path]::GetFullPath('D:\Owned Player\PocketBump.exe')
}
function New-IdentityObservation([AllowNull()][string]$Path = '') {
    return [pscustomobject]@{
        process_id=$expected.process_id; start_time_utc_ticks=$expected.start_time_utc_ticks
        executable=$Path; exited=$false
    }
}
function Assert-IdentityFixture([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw "Identity fixture failed: $Label" }
    $script:identityChecks++
}
function Invoke-IdentityFixture([object[]]$Observations, [int]$Limit = 200, [string]$ExpectedError = '') {
    $state = [pscustomobject]@{ reads=0; waits=0; elapsed=0 }
    $read = {
        $index = [Math]::Min($state.reads, $Observations.Length - 1)
        $state.reads++
        $value = $Observations[$index]
        if ($value -is [Exception]) { throw $value }
        return $value
    }
    $elapsed = { $state.elapsed }
    $wait = { $state.waits++; $state.elapsed += 50 }
    $failure = $null
    try {
        Wait-DashPresentationStartupIdentity -Ticket $expected -ReadObservation $read `
            -RetryMilliseconds $Limit -ElapsedMilliseconds $elapsed -WaitInterval $wait
    }
    catch { $failure = $_ }
    if ($ExpectedError.Length -eq 0) {
        Assert-IdentityFixture ($null -eq $failure) "Expected success; got $($failure.Exception.Message)"
    }
    else {
        Assert-IdentityFixture ($null -ne $failure -and $failure.Exception.Message -like ('*' + $ExpectedError + '*')) `
            "Expected '$ExpectedError'; got $($failure.Exception.Message)"
    }
    return $state
}

$valid = New-IdentityObservation $expected.executable
$state = Invoke-IdentityFixture @($valid)
Assert-IdentityFixture ($state.reads -eq 1 -and $state.waits -eq 0) 'ready executable is verified immediately'

$state = Invoke-IdentityFixture @((New-IdentityObservation ''), (New-IdentityObservation $null), $valid)
Assert-IdentityFixture ($state.reads -eq 3 -and $state.waits -eq 2 -and $state.elapsed -eq 100) 'empty module path alone retries then succeeds'

$caseVariant = New-IdentityObservation 'd:\OWNED PLAYER\pocketbump.exe'
$state = Invoke-IdentityFixture @($caseVariant)
Assert-IdentityFixture ($state.waits -eq 0) 'exact Windows path comparison remains case insensitive'

$state = Invoke-IdentityFixture @((New-IdentityObservation '')) 200 'remained unavailable'
Assert-IdentityFixture ($state.reads -eq 5 -and $state.waits -eq 4 -and $state.elapsed -eq 200) 'repeated empty path has a bounded deadline'
$state = Invoke-IdentityFixture @((New-IdentityObservation '')) 0 'remained unavailable'
Assert-IdentityFixture ($state.reads -eq 1 -and $state.waits -eq 0) 'zero retry budget fails immediately'

foreach ($change in @('pid','start','exit')) {
    $invalid = New-IdentityObservation ''
    switch ($change) {
        'pid' { $invalid.process_id++ }
        'start' { $invalid.start_time_utc_ticks++ }
        'exit' { $invalid.exited=$true }
    }
    $state = Invoke-IdentityFixture @($invalid,$valid) 200 'exited or its PID/start time changed'
    Assert-IdentityFixture ($state.reads -eq 1 -and $state.waits -eq 0) "$change mismatch is never disguised as empty module startup"
}

$wrongPath = New-IdentityObservation 'D:\Another Player\PocketBump.exe'
$state = Invoke-IdentityFixture @($wrongPath,$valid) 200 'executable does not match'
Assert-IdentityFixture ($state.reads -eq 1 -and $state.waits -eq 0) 'wrong executable path fails immediately'
$state = Invoke-IdentityFixture @((New-IdentityObservation ''),$wrongPath,$valid) 200 'executable does not match'
Assert-IdentityFixture ($state.reads -eq 2 -and $state.waits -eq 1) 'a mismatch during startup stops further retry'

$state = Invoke-IdentityFixture @((New-IdentityObservation '   '),$valid) 200 'executable does not match'
Assert-IdentityFixture ($state.reads -eq 1 -and $state.waits -eq 0) 'whitespace path is invalid rather than transient empty'

foreach ($exception in @(
    [ComponentModel.Win32Exception]::new(5, 'fixture access denied'),
    [InvalidOperationException]::new('fixture process has exited'),
    [UnauthorizedAccessException]::new('fixture denied module enumeration')
)) {
    $state = Invoke-IdentityFixture @($exception,$valid) 200 $exception.Message
    Assert-IdentityFixture ($state.reads -eq 1 -and $state.waits -eq 0) 'getter exception propagates without retry'
}

Assert-IdentityFixture (!(Test-DashPresentationIdentityObservation $expected (New-IdentityObservation ''))) 'single observation explicitly reports module not ready'
Assert-IdentityFixture (Test-DashPresentationIdentityObservation $expected $valid) 'single observation requires complete identity'
Write-Host "Presentation identity pure fixtures passed: $script:identityChecks. No processes or desktop APIs were accessed."
