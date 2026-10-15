# Read-only observer for DashNetworkReportStore's immutable, sequenced generations.
# Construct once BEFORE launching this invocation's Players. Existing filenames
# are excluded without reading JSON; neither timestamps nor file ages are used.
# The writer publishes <guid32>-<sequence:D10>-session.json via File.Move and
# never rewrites a published path. It may prune older generations while we read.

function Get-ReleaseReportObserverPaths($Observer) {
    $Observer.counters.directory_scans++
    try {
        if (![IO.Directory]::Exists($Observer.report_directory)) { return }
        foreach ($path in [IO.Directory]::GetFiles($Observer.report_directory, '*-session.json', [IO.SearchOption]::TopDirectoryOnly)) {
            $Observer.counters.enumerated_paths++
            $path
        }
    }
    catch [IO.DirectoryNotFoundException] { return }
    catch {
        $Observer.counters.enumeration_failures++
        throw
    }
}

function New-ReleaseReportObserver {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)][ValidateNotNullOrEmpty()][string]$ReportDirectory)
    $observer = [pscustomobject]@{
        report_directory = [IO.Path]::GetFullPath($ReportDirectory)
        baseline_paths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
        cached_reports = [Collections.Generic.Dictionary[string,object]]::new([StringComparer]::OrdinalIgnoreCase)
        parse_attempts_by_path = [Collections.Generic.Dictionary[string,long]]::new([StringComparer]::OrdinalIgnoreCase)
        counters = [pscustomobject]@{
            directory_scans = 0L; enumerated_paths = 0L; baseline_skips = 0L; cached_path_skips = 0L
            unrecognized_path_skips = 0L; read_attempts = 0L; read_failures = 0L
            parse_attempts = 0L; parse_failures = 0L; parsed_reports = 0L; enumeration_failures = 0L
        }
    }
    foreach ($path in @(Get-ReleaseReportObserverPaths $observer)) { $null = $observer.baseline_paths.Add($path) }
    return $observer
}

function Get-ObservedReleaseReport {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)]$Observer,
        [Parameter(Mandatory = $true)]$Ticket
    )
    $ticketProcessId = [long]$Ticket.process_id
    $ticketStartedTicks = [long]$Ticket.start_time_utc_ticks
    if ($ticketProcessId -le 0 -or $ticketStartedTicks -le 0) { throw 'A process identity ticket with PID and UTC start ticks is required.' }

    $presentPaths = [Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach ($path in @(Get-ReleaseReportObserverPaths $Observer)) {
        $null = $presentPaths.Add($path)
        if ($Observer.baseline_paths.Contains($path)) { $Observer.counters.baseline_skips++; continue }
        if ($Observer.cached_reports.ContainsKey($path)) { $Observer.counters.cached_path_skips++; continue }
        $name = [IO.Path]::GetFileName($path)
        # Do not cache mutable legacy names, .pending files or failure snapshots.
        if ($name -notmatch '^(?<report>[0-9a-fA-F]{32})-(?<sequence>[0-9]{10})-session\.json$') {
            $Observer.counters.unrecognized_path_skips++
            continue
        }
        $pathReportId = $Matches.report
        $pathSequence = [long]$Matches.sequence
        $Observer.counters.read_attempts++
        try { $json = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8) }
        catch {
            # Pruning, a temporarily locked file or interrupted publication can
            # race enumeration. Do not mark it seen; retry if it reappears.
            $Observer.counters.read_failures++
            continue
        }
        $Observer.counters.parse_attempts++
        if (!$Observer.parse_attempts_by_path.ContainsKey($path)) { $Observer.parse_attempts_by_path[$path] = 0L }
        $Observer.parse_attempts_by_path[$path]++
        try {
            $value = ConvertFrom-Json -InputObject $json -ErrorAction Stop
            if ($null -eq $value -or $value -is [array] -or $value.schema -cne 'pocketbump_lan_release_v1' -or
                $value.report_id -ine $pathReportId -or [long]$value.write_sequence -ne $pathSequence -or
                $pathSequence -lt 1 -or $pathSequence -gt [int]::MaxValue -or [long]$value.process_id -le 0) {
                throw 'Report identity does not match its immutable generation filename.'
            }
            $started = [DateTime]::Parse([string]$value.session_started_utc, [Globalization.CultureInfo]::InvariantCulture,
                [Globalization.DateTimeStyles]::RoundtripKind)
            if ($started.Kind -ne [DateTimeKind]::Utc) { throw 'Report session start must explicitly identify UTC.' }
            $observation = [pscustomobject]@{
                path = $path; data = $value; started = $started; sequence = [int]$value.write_sequence
            }
            $Observer.cached_reports.Add($path, $observation)
            $Observer.counters.parsed_reports++
        }
        catch {
            # Partial/malformed JSON (including incomplete required identity) is
            # never cached. A subsequent poll can read a complete publication.
            $Observer.counters.parse_failures++
        }
    }

    $latest = $null
    foreach ($observation in $Observer.cached_reports.Values) {
        # Cache avoids reparsing, not current-file validation. Pruned/deleted
        # generations must not become a permanent stale answer to a later wait.
        if (!$presentPaths.Contains($observation.path) -or ![IO.File]::Exists($observation.path)) { continue }
        if ($observation.data.process_id -ne $ticketProcessId -or $observation.started.Ticks -lt $ticketStartedTicks) { continue }
        if ($null -eq $latest -or $observation.started -gt $latest.started -or
            ($observation.started -eq $latest.started -and $observation.sequence -gt $latest.sequence)) {
            $latest = $observation
        }
    }
    return $latest
}

function Get-ReleaseReportObserverStats {
    [CmdletBinding()]
    param([Parameter(Mandatory = $true)]$Observer)
    $attempts = [ordered]@{}
    foreach ($path in $Observer.parse_attempts_by_path.Keys) { $attempts[$path] = $Observer.parse_attempts_by_path[$path] }
    # Return a detached snapshot so recording stats cannot change the observer.
    [pscustomobject]@{
        schema = 'pocketbump_release_report_observer_v1'
        report_directory = $Observer.report_directory
        baseline_files = $Observer.baseline_paths.Count
        cached_reports = $Observer.cached_reports.Count
        directory_scans = $Observer.counters.directory_scans
        enumerated_paths = $Observer.counters.enumerated_paths
        baseline_skips = $Observer.counters.baseline_skips
        cached_path_skips = $Observer.counters.cached_path_skips
        unrecognized_path_skips = $Observer.counters.unrecognized_path_skips
        read_attempts = $Observer.counters.read_attempts
        read_failures = $Observer.counters.read_failures
        parse_attempts = $Observer.counters.parse_attempts
        parse_failures = $Observer.counters.parse_failures
        parsed_reports = $Observer.counters.parsed_reports
        enumeration_failures = $Observer.counters.enumeration_failures
        parse_attempts_by_path = [pscustomobject]$attempts
    }
}
