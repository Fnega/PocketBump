[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
if ($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -ne 1) {
    throw 'Run this fixture with Windows PowerShell 5.1 (powershell.exe).'
}
. (Join-Path $PSScriptRoot 'ReleaseReportObserver.ps1')

$fixtureRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot ('ReleaseResults/ReleaseReportObserverFixture-' +
    [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N'))))
$null = [IO.Directory]::CreateDirectory($fixtureRoot)
$utf8 = [Text.UTF8Encoding]::new($false)
$checks = 0
function Assert-ObserverTrue([bool]$Condition, [string]$Label) {
    if (!$Condition) { throw ('Release report observer fixture failed: ' + $Label) }
    $script:checks++
}
function Assert-ObserverOwnedPath([string]$Path) {
    $resolved = [IO.Path]::GetFullPath($Path)
    if (!$resolved.StartsWith($fixtureRoot + '\', [StringComparison]::OrdinalIgnoreCase)) { throw 'Fixture operation escaped its unique directory.' }
    return $resolved
}
function Write-ObserverFixtureText([string]$Path, [string]$Text) {
    $owned = Assert-ObserverOwnedPath $Path
    $null = [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($owned))
    [IO.File]::WriteAllText($owned, $Text, $utf8)
}
function New-ObserverFixtureReport([string]$Directory, [string]$ReportId, [int]$Sequence, [int]$ProcessId,
    [DateTime]$Started, [string]$Role = 'host') {
    $path = Join-Path $Directory ($ReportId + '-' + $Sequence.ToString('D10') + '-session.json')
    $value = [pscustomobject]@{
        schema = 'pocketbump_lan_release_v1'; report_id = $ReportId; write_sequence = $Sequence
        process_id = $ProcessId; session_started_utc = $Started.ToUniversalTime().ToString('O'); role = $Role
        simulation_tick = $Sequence * 10; ready = $true
    }
    Write-ObserverFixtureText $path ($value | ConvertTo-Json -Depth 4)
    return $path
}

try {
    $directory = Join-Path $fixtureRoot 'reports'
    $null = [IO.Directory]::CreateDirectory($directory)
    $started = [DateTime]::SpecifyKind([DateTime]::Parse('2026-10-15T12:00:00'), [DateTimeKind]::Utc)
    $ticket = [pscustomobject]@{ process_id = 4242; start_time_utc_ticks = $started.Ticks }
    $oldPaths = New-Object Collections.Generic.List[string]
    for ($i = 0; $i -lt 1000; $i++) {
        $oldPaths.Add((New-ObserverFixtureReport $directory ([Guid]::NewGuid().ToString('N')) 1 4242 $started.AddDays(-1)))
    }
    # Snapshot names only, even when a preexisting JSON file is malformed.
    Write-ObserverFixtureText $oldPaths[0] '{ incomplete old JSON'
    $observer = New-ReleaseReportObserver -ReportDirectory $directory
    $stats = Get-ReleaseReportObserverStats $observer
    Assert-ObserverTrue ($stats.baseline_files -eq 1000 -and $stats.read_attempts -eq 0 -and $stats.parse_attempts -eq 0) '1000 existing JSON files are snapshotted without content reads'
    Assert-ObserverTrue ($null -eq (Get-ObservedReleaseReport $observer $ticket)) 'historical same-PID reports do not match'
    $stats = Get-ReleaseReportObserverStats $observer
    Assert-ObserverTrue ($stats.read_attempts -eq 0 -and $stats.parse_attempts -eq 0) 'polling performs zero old JSON reads or parses'

    $session = [Guid]::NewGuid().ToString('N')
    $first = New-ObserverFixtureReport $directory $session 1 4242 $started.AddSeconds(1)
    # Metadata can be old independently of session identity; never filter by mtime.
    [IO.File]::SetLastWriteTimeUtc((Assert-ObserverOwnedPath $first), $started.AddYears(-2))
    $found = Get-ObservedReleaseReport $observer $ticket
    Assert-ObserverTrue ($found.path -eq $first -and $found.sequence -eq 1 -and $found.data.role -eq 'host') 'new path with old mtime is observed'
    Assert-ObserverTrue ($found.started.Kind -eq [DateTimeKind]::Utc -and $found.started.Ticks -eq $started.AddSeconds(1).Ticks) 'original started and result shape are retained'
    $null = Get-ObservedReleaseReport $observer $ticket
    $stats = Get-ReleaseReportObserverStats $observer
    Assert-ObserverTrue ($stats.parse_attempts -eq 1 -and $stats.cached_reports -eq 1 -and $stats.cached_path_skips -gt 0) 'repeated poll does not parse immutable generation again'

    $seq10 = New-ObserverFixtureReport $directory $session 10 4242 $started.AddSeconds(1)
    $null = New-ObserverFixtureReport $directory $session 2 4242 $started.AddSeconds(1)
    Assert-ObserverTrue ((Get-ObservedReleaseReport $observer $ticket).path -eq $seq10) 'same-session sequence sorting is numeric'
    $newSession = [Guid]::NewGuid().ToString('N')
    $newest = New-ObserverFixtureReport $directory $newSession 1 4242 $started.AddSeconds(3) 'join'
    $found = Get-ObservedReleaseReport $observer $ticket
    Assert-ObserverTrue ($found.path -eq $newest -and $found.sequence -eq 1 -and $found.data.role -eq 'join') 'new role/session outranks older session with higher sequence'
    $null = New-ObserverFixtureReport $directory ([Guid]::NewGuid().ToString('N')) 100 7777 $started.AddMinutes(5)
    $null = New-ObserverFixtureReport $directory ([Guid]::NewGuid().ToString('N')) 999 4242 $started.AddSeconds(-1)
    Assert-ObserverTrue ((Get-ObservedReleaseReport $observer $ticket).path -eq $newest) 'wrong PID and same PID from before process launch cannot win'
    $reuseTicket = [pscustomobject]@{ process_id = 4242; start_time_utc_ticks = $started.AddMinutes(10).Ticks }
    Assert-ObserverTrue ($null -eq (Get-ObservedReleaseReport $observer $reuseTicket)) 'PID reuse rejects cached sessions from a prior launch'
    $otherTicket = [pscustomobject]@{ process_id = 9999; start_time_utc_ticks = $started.Ticks }
    Assert-ObserverTrue ($null -eq (Get-ObservedReleaseReport $observer $otherTicket)) 'unrelated process never matches'
    $equalTicket = [pscustomobject]@{ process_id = 4242; start_time_utc_ticks = $started.AddSeconds(3).Ticks }
    Assert-ObserverTrue ((Get-ObservedReleaseReport $observer $equalTicket).path -eq $newest) 'session start equal to process start is accepted'

    $partialId = [Guid]::NewGuid().ToString('N')
    $partial = Join-Path $directory ($partialId + '-0000000001-session.json')
    Write-ObserverFixtureText $partial '{"schema":'
    Assert-ObserverTrue ((Get-ObservedReleaseReport $observer $ticket).path -eq $newest) 'malformed new JSON does not hide last complete observation'
    $null = New-ObserverFixtureReport $directory $partialId 1 4242 $started.AddSeconds(4)
    Assert-ObserverTrue ((Get-ObservedReleaseReport $observer $ticket).path -eq $partial) 'malformed path is retried after becoming complete'
    $stats = Get-ReleaseReportObserverStats $observer
    Assert-ObserverTrue ($stats.parse_attempts_by_path.$partial -eq 2 -and $stats.parse_failures -eq 1) 'retry parse attempts are independently counted'
    $null = Get-ObservedReleaseReport $observer $ticket
    Assert-ObserverTrue ((Get-ReleaseReportObserverStats $observer).parse_attempts -eq $stats.parse_attempts) 'repaired valid report is subsequently cached'
    foreach ($old in $oldPaths) {
        Assert-ObserverTrue (!$observer.parse_attempts_by_path.ContainsKey($old)) 'no baseline path was ever parsed'
    }
    $stats.parse_attempts = -100
    Assert-ObserverTrue ((Get-ReleaseReportObserverStats $observer).parse_attempts -ge 1) 'stats snapshot cannot mutate observer counters'

    # A cached generation is not current after the writer prunes its source file.
    [IO.File]::Delete((Assert-ObserverOwnedPath $partial))
    Assert-ObserverTrue ((Get-ObservedReleaseReport $observer $ticket).path -eq $newest) 'pruned cached observation is excluded from current selection'

    $missingDirectory = Join-Path $fixtureRoot 'initially-missing'
    $missing = New-ReleaseReportObserver $missingDirectory
    Assert-ObserverTrue (!(Test-Path -LiteralPath $missingDirectory)) 'observer does not create an absent report directory'
    Assert-ObserverTrue ($null -eq (Get-ObservedReleaseReport $missing $ticket)) 'missing directory returns null'
    $appeared = New-ObserverFixtureReport $missingDirectory ([Guid]::NewGuid().ToString('N')) 1 4242 $started
    Assert-ObserverTrue ((Get-ObservedReleaseReport $missing $ticket).path -eq $appeared) 'directory and report appearing after startup are discovered'

    # Deterministic enumeration/read race: override only the name-enumeration
    # function for this synthetic directory, then delete its own just-listed file.
    $raceDirectory = Join-Path $fixtureRoot 'read-race'
    $raceObserver = New-ReleaseReportObserver $raceDirectory
    $raceId = [Guid]::NewGuid().ToString('N')
    $racePath = New-ObserverFixtureReport $raceDirectory $raceId 1 4242 $started
    $originalEnumerator = ${function:Get-ReleaseReportObserverPaths}
    try {
        function Get-ReleaseReportObserverPaths($Observer) {
            $paths = @(& $originalEnumerator $Observer)
            if ($Observer.report_directory -eq $raceDirectory -and [IO.File]::Exists($racePath)) {
                [IO.File]::Delete((Assert-ObserverOwnedPath $racePath))
            }
            $paths
        }
        Assert-ObserverTrue ($null -eq (Get-ObservedReleaseReport $raceObserver $ticket)) 'file disappearing between enumeration and read is harmless'
    }
    finally { Set-Item -LiteralPath Function:Get-ReleaseReportObserverPaths -Value $originalEnumerator }
    $raceStats = Get-ReleaseReportObserverStats $raceObserver
    Assert-ObserverTrue ($raceStats.read_attempts -eq 1 -and $raceStats.read_failures -eq 1 -and $raceStats.parse_attempts -eq 0) 'disappearance records read failure without a phantom JSON parse'
    $null = New-ObserverFixtureReport $raceDirectory $raceId 1 4242 $started
    Assert-ObserverTrue ((Get-ObservedReleaseReport $raceObserver $ticket).path -eq $racePath) 'disappeared path can be read on a later poll'

    $invalidDirectory = Join-Path $fixtureRoot 'identity-validation'
    $invalidObserver = New-ReleaseReportObserver $invalidDirectory
    $validId = [Guid]::NewGuid().ToString('N')
    $validationPath = New-ObserverFixtureReport $invalidDirectory $validId 1 4242 $started
    $validJson = [IO.File]::ReadAllText($validationPath)
    foreach ($invalidField in @('report_id', 'write_sequence', 'session_started_utc', 'missing_started')) {
        $invalid = $validJson | ConvertFrom-Json
        switch ($invalidField) {
            'report_id' { $invalid.report_id = [Guid]::NewGuid().ToString('N') }
            'write_sequence' { $invalid.write_sequence = 2 }
            'session_started_utc' { $invalid.session_started_utc = '2026-10-15T20:00:00.0000000+08:00' }
            'missing_started' { $invalid.PSObject.Properties.Remove('session_started_utc') }
        }
        Write-ObserverFixtureText $validationPath ($invalid | ConvertTo-Json)
        Assert-ObserverTrue ($null -eq (Get-ObservedReleaseReport $invalidObserver $ticket)) ('invalid immutable report identity is rejected: ' + $invalidField)
    }
    Assert-ObserverTrue ((Get-ReleaseReportObserverStats $invalidObserver).parse_failures -eq 4) 'all incomplete or mismatched identity records remain retryable'
    Write-ObserverFixtureText $validationPath $validJson
    Assert-ObserverTrue ((Get-ObservedReleaseReport $invalidObserver $ticket).path -eq $validationPath) 'valid identity after failed generations can be observed'
    $legacyPath = Join-Path $invalidDirectory ($validId + '-session.json')
    Write-ObserverFixtureText $legacyPath $validJson
    $null = Get-ObservedReleaseReport $invalidObserver $ticket
    Assert-ObserverTrue (!$invalidObserver.parse_attempts_by_path.ContainsKey($legacyPath) -and
        (Get-ReleaseReportObserverStats $invalidObserver).unrecognized_path_skips -eq 1) 'mutable legacy session filename is never parsed or cached'
    [IO.File]::Delete((Assert-ObserverOwnedPath $validationPath))
    Assert-ObserverTrue ($null -eq (Get-ObservedReleaseReport $invalidObserver $ticket)) 'removing every current immutable report cannot leave a stale cached answer'

    [pscustomobject]@{
        status = 'passed'; checks = $checks; powershell_version = $PSVersionTable.PSVersion.ToString()
        fixture = $fixtureRoot; historical_json_files = 1000; historical_json_parses = 0
        observer_stats = Get-ReleaseReportObserverStats $observer
        scope = 'Synthetic read-only report observation contracts; no Unity, Player, desktop or real report writes.'
        cleanup = 'Unique project-local fixture retained for audit; no real reports changed or deleted.'
    } | ConvertTo-Json -Depth 6
}
catch { Write-Warning ('Failed fixture retained: ' + $fixtureRoot); throw }
