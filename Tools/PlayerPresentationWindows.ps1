# Opt-in window positioning for this invocation's actual presentation probes only.
# Dot-sourcing and geometry/identity checks never display or move a window.
# The launcher must keep Hidden as its default and require explicit user-approved
# -CapturePresentation -ShowPresentationWindows before calling the action entrypoint.

function Initialize-DashPresentationWindowApi {
    if ('PocketBumpDiagnostics.PresentationWindows' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
namespace PocketBumpDiagnostics {
    [StructLayout(LayoutKind.Sequential)] public struct PixelRect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] public struct MonitorInfo { public int Size; public PixelRect Monitor, Work; public uint Flags; }
    public sealed class WindowInfo {
        public IntPtr Handle; public uint ProcessId; public string ClassName;
        public PixelRect Outer, Client; public bool Visible, Minimized;
    }
    public static class PresentationWindows {
        delegate bool WindowCallback(IntPtr handle, IntPtr data);
        delegate bool MonitorCallback(IntPtr monitor, IntPtr dc, ref PixelRect rect, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumWindows(WindowCallback callback, IntPtr data);
        [DllImport("user32.dll")] static extern bool EnumDisplayMonitors(IntPtr dc, IntPtr rect, MonitorCallback callback, IntPtr data);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr handle, StringBuilder name, int count);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle, out uint processId);
        [DllImport("user32.dll")] static extern IntPtr GetWindow(IntPtr handle, uint command);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr handle);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr handle);
        [DllImport("user32.dll", SetLastError=true)] static extern bool GetWindowRect(IntPtr handle, out PixelRect rect);
        [DllImport("user32.dll", SetLastError=true)] static extern bool GetClientRect(IntPtr handle, out PixelRect rect);
        [DllImport("user32.dll", SetLastError=true)] static extern bool GetMonitorInfo(IntPtr handle, ref MonitorInfo info);
        [DllImport("user32.dll", SetLastError=true)] public static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
        [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr handle, int command);
        [DllImport("user32.dll", SetLastError=true)] public static extern bool SetWindowPos(IntPtr handle, IntPtr after, int x, int y, int width, int height, uint flags);
        public static WindowInfo Read(IntPtr handle) {
            if (!IsWindow(handle)) throw new InvalidOperationException("Player HWND no longer exists.");
            uint owner; GetWindowThreadProcessId(handle, out owner);
            var name = new StringBuilder(256); GetClassName(handle, name, name.Capacity);
            PixelRect outer, client;
            if (!GetWindowRect(handle, out outer) || !GetClientRect(handle, out client)) throw new Win32Exception();
            return new WindowInfo { Handle=handle, ProcessId=owner, ClassName=name.ToString(), Outer=outer, Client=client, Visible=IsWindowVisible(handle), Minimized=IsIconic(handle) };
        }
        public static WindowInfo[] Find(uint processId) {
            var found = new List<WindowInfo>();
            EnumWindows(delegate(IntPtr handle, IntPtr data) {
                uint owner; GetWindowThreadProcessId(handle, out owner);
                if (owner != processId || GetWindow(handle, 4) != IntPtr.Zero) return true;
                try {
                    var info = Read(handle);
                    if (info.ClassName == "UnityWndClass") found.Add(info);
                } catch (InvalidOperationException) { } catch (Win32Exception) { }
                return true;
            }, IntPtr.Zero);
            return found.ToArray();
        }
        public static PixelRect[] WorkAreas() {
            var areas = new List<PixelRect>();
            if (!EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, delegate(IntPtr handle, IntPtr dc, ref PixelRect rect, IntPtr data) {
                var info = new MonitorInfo(); info.Size = Marshal.SizeOf(typeof(MonitorInfo));
                if (GetMonitorInfo(handle, ref info)) areas.Add(info.Work);
                return true;
            }, IntPtr.Zero)) throw new Win32Exception();
            return areas.ToArray();
        }
    }
}
'@ -ErrorAction Stop
}

function New-DashPresentationWindowTicket {
    param([Parameter(Mandatory = $true)][Diagnostics.Process]$Process,
        [Parameter(Mandatory = $true)][string]$ExpectedPlayerPath)
    $Process.Refresh()
    if ($Process.HasExited) { throw 'Cannot register an exited presentation Player.' }
    $ticket = [pscustomobject]@{
        process = $Process; process_id = $Process.Id
        start_time_utc_ticks = $Process.StartTime.ToUniversalTime().Ticks
        executable = [IO.Path]::GetFullPath($ExpectedPlayerPath)
        window_handle = [IntPtr]::Zero
    }
    # A newly started process can expose its start time before module enumeration
    # yields a path. Only this empty-path startup observation is retryable.
    Wait-DashPresentationStartupIdentity -Ticket $ticket -ReadObservation { Read-DashPresentationIdentityObservation $ticket }
    return $ticket
}

function Read-DashPresentationIdentityObservation($Ticket) {
    if ($null -eq $Ticket -or $Ticket.process -isnot [Diagnostics.Process]) { throw "A ticket from this invocation's Start-Process object is required." }
    $Ticket.process.Refresh()
    if ($Ticket.process.HasExited -or $Ticket.process.Id -ne $Ticket.process_id) { throw 'The registered presentation Player exited or changed identity.' }
    $current = [Diagnostics.Process]::GetProcessById($Ticket.process_id)
    try {
        if ($current.get_HasExited()) { throw 'The registered presentation Player exited during identity verification.' }
        $started = $current.get_StartTime().ToUniversalTime().Ticks
        if ($started -ne $Ticket.start_time_utc_ticks) {
            throw 'Player PID reuse, changed start time, or unexpected executable; refusing window access.'
        }
        # Explicit getter calls preserve access-denied/module-enumeration exceptions;
        # such errors must never become a retryable empty path.
        $module = $current.get_MainModule()
        $path = if ($null -eq $module) { '' } else { $module.get_FileName() }
        return [pscustomobject]@{ process_id=$current.Id; start_time_utc_ticks=$started; executable=$path; exited=$false }
    }
    finally { $current.Dispose() }
}

function Test-DashPresentationIdentityObservation($Ticket, $Observation) {
    if ($null -eq $Observation -or $Observation.exited -or $Observation.process_id -ne $Ticket.process_id -or
        $Observation.start_time_utc_ticks -ne $Ticket.start_time_utc_ticks) {
        throw 'Player exited or its PID/start time changed; refusing window access.'
    }
    if ([string]::IsNullOrEmpty($Observation.executable)) { return $false }
    if ([string]::IsNullOrWhiteSpace($Observation.executable) -or
        ![string]::Equals([IO.Path]::GetFullPath($Observation.executable), $Ticket.executable, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'Player executable does not match its registered path; refusing window access.'
    }
    return $true
}

function Wait-DashPresentationStartupIdentity {
    param([Parameter(Mandatory = $true)]$Ticket, [Parameter(Mandatory = $true)][scriptblock]$ReadObservation,
        [ValidateRange(0, 5000)][int]$RetryMilliseconds = 2000,
        [scriptblock]$ElapsedMilliseconds, [scriptblock]$WaitInterval)
    # Injected reads/time/waits let tests exercise retries without any process or
    # desktop access. Production uses the exact Start-Process ticket and a stopwatch.
    $watch = [Diagnostics.Stopwatch]::StartNew()
    if ($null -eq $ElapsedMilliseconds) { $ElapsedMilliseconds = { $watch.Elapsed.TotalMilliseconds } }
    if ($null -eq $WaitInterval) { $WaitInterval = { Start-Sleep -Milliseconds 50 } }
    while ($true) {
        $observation = & $ReadObservation
        if (Test-DashPresentationIdentityObservation $Ticket $observation) { return }
        if ((& $ElapsedMilliseconds) -ge $RetryMilliseconds) {
            throw "Player executable path remained unavailable during the ${RetryMilliseconds}ms startup identity check; no window access was granted."
        }
        & $WaitInterval
    }
}

function Assert-DashPresentationWindowIdentity($Ticket) {
    $observation = Read-DashPresentationIdentityObservation $Ticket
    if (!(Test-DashPresentationIdentityObservation $Ticket $observation)) {
        throw 'Registered Player executable path is unavailable; refusing window access without retry.'
    }
}

function Get-DashPresentationOwnedWindow($Ticket) {
    Assert-DashPresentationWindowIdentity $Ticket
    Initialize-DashPresentationWindowApi
    if ($Ticket.window_handle -ne [IntPtr]::Zero) {
        $window = [PocketBumpDiagnostics.PresentationWindows]::Read($Ticket.window_handle)
        if ($window.ProcessId -ne $Ticket.process_id -or $window.ClassName -cne 'UnityWndClass') {
            throw 'The retained HWND no longer belongs to the registered Unity Player.'
        }
        return $window
    }
    $windows = @([PocketBumpDiagnostics.PresentationWindows]::Find($Ticket.process_id))
    if ($windows.Count -gt 1) { throw 'Multiple unowned Unity windows found for one Player; refusing to guess a target.' }
    if ($windows.Count -eq 1) { return $windows[0] }
    return $null
}

function Get-DashPresentationWindowPlacement {
    param([Parameter(Mandatory = $true)][int]$FirstWidth, [Parameter(Mandatory = $true)][int]$FirstHeight,
        [Parameter(Mandatory = $true)][int]$SecondWidth, [Parameter(Mandatory = $true)][int]$SecondHeight,
        [Parameter(Mandatory = $true)][object[]]$WorkAreas)
    if ([Math]::Min([Math]::Min($FirstWidth, $FirstHeight), [Math]::Min($SecondWidth, $SecondHeight)) -le 0) { throw 'Window dimensions must be positive.' }
    # Opposite corners keep both full outer rectangles inside a work area. On
    # smaller displays partial overlap is allowed, but full coverage is rejected.
    foreach ($areaFirst in $WorkAreas) {
        foreach ($areaSecond in $WorkAreas) {
            if ($FirstWidth -gt $areaFirst.Right - $areaFirst.Left -or $FirstHeight -gt $areaFirst.Bottom - $areaFirst.Top -or
                $SecondWidth -gt $areaSecond.Right - $areaSecond.Left -or $SecondHeight -gt $areaSecond.Bottom - $areaSecond.Top) { continue }
            $x0 = $areaFirst.Left; $y0 = $areaFirst.Top
            $x1 = $areaSecond.Right - $SecondWidth; $y1 = $areaSecond.Bottom - $SecondHeight
            $overlap = [Math]::Max(0, [Math]::Min($x0 + $FirstWidth, $x1 + $SecondWidth) - [Math]::Max($x0, $x1)) *
                [Math]::Max(0, [Math]::Min($y0 + $FirstHeight, $y1 + $SecondHeight) - [Math]::Max($y0, $y1))
            if ($overlap -ge [Math]::Min($FirstWidth * $FirstHeight, $SecondWidth * $SecondHeight)) { continue }
            return [pscustomobject]@{ first_x = $x0; first_y = $y0; second_x = $x1; second_y = $y1; overlap_pixels = $overlap }
        }
    }
    throw 'No placement keeps both requested-size windows on-screen without fully covering one; no resizing or off-screen workaround is permitted.'
}

function Show-DashPresentationPair {
    param([Parameter(Mandatory = $true)]$FirstTicket, [Parameter(Mandatory = $true)]$SecondTicket,
        [Parameter(Mandatory = $true)][int]$ExpectedWidth, [Parameter(Mandatory = $true)][int]$ExpectedHeight,
        [switch]$CapturePresentation, [switch]$ShowPresentationWindows, [ValidateRange(1, 30)][int]$TimeoutSeconds = 10)
    if (!$CapturePresentation -or !$ShowPresentationWindows) { throw 'Explicit user-approved CapturePresentation and ShowPresentationWindows are required; no window was changed.' }
    if ($FirstTicket.process_id -eq $SecondTicket.process_id) { throw 'Two distinct invocation-owned Player processes are required.' }
    Initialize-DashPresentationWindowApi
    $previousDpi = [PocketBumpDiagnostics.PresentationWindows]::SetThreadDpiAwarenessContext([IntPtr](-4))
    if ($previousDpi -eq [IntPtr]::Zero) { throw 'Cannot establish physical-pixel window coordinates; refusing window changes.' }
    try {
        $deadline = [DateTime]::UtcNow.AddSeconds($TimeoutSeconds)
        do {
            $first = Get-DashPresentationOwnedWindow $FirstTicket
            $second = Get-DashPresentationOwnedWindow $SecondTicket
            $ready = $null -ne $first -and $null -ne $second -and
                $first.Client.Right - $first.Client.Left -eq $ExpectedWidth -and $first.Client.Bottom - $first.Client.Top -eq $ExpectedHeight -and
                $second.Client.Right - $second.Client.Left -eq $ExpectedWidth -and $second.Client.Bottom - $second.Client.Top -eq $ExpectedHeight
            if ($ready) { break }
            Start-Sleep -Milliseconds 50
        } while ([DateTime]::UtcNow -lt $deadline)
        if (!$ready) { throw 'Both invocation-owned Player windows did not reach the requested client dimensions in time.' }
        $FirstTicket.window_handle = $first.Handle; $SecondTicket.window_handle = $second.Handle
        $placement = Get-DashPresentationWindowPlacement -FirstWidth ($first.Outer.Right - $first.Outer.Left) -FirstHeight ($first.Outer.Bottom - $first.Outer.Top) `
            -SecondWidth ($second.Outer.Right - $second.Outer.Left) -SecondHeight ($second.Outer.Bottom - $second.Outer.Top) `
            -WorkAreas ([PocketBumpDiagnostics.PresentationWindows]::WorkAreas())
        foreach ($operation in @(@($FirstTicket, $placement.first_x, $placement.first_y), @($SecondTicket, $placement.second_x, $placement.second_y))) {
            $ticket = $operation[0]
            $window = Get-DashPresentationOwnedWindow $ticket
            # SW_SHOWNOACTIVATE; its Boolean return describes PREVIOUS visibility,
            # not success. Verify actual state after the position request instead.
            [void][PocketBumpDiagnostics.PresentationWindows]::ShowWindow($window.Handle, 4)
            $window = Get-DashPresentationOwnedWindow $ticket
            # HWND_TOP, SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW. This is not
            # TOPMOST and does not change resolution, styles, preferences or focus.
            if (![PocketBumpDiagnostics.PresentationWindows]::SetWindowPos($window.Handle, [IntPtr]::Zero, $operation[1], $operation[2], 0, 0, 0x0051)) {
                throw ('Could not position the registered Player window; Win32 error ' + [Runtime.InteropServices.Marshal]::GetLastWin32Error())
            }
            $window = Get-DashPresentationOwnedWindow $ticket
            if ($window.Outer.Left -ne $operation[1] -or $window.Outer.Top -ne $operation[2]) {
                throw 'The registered Player did not accept its in-bounds position; refusing to claim non-occluded evidence.'
            }
        }
        $observations = @()
        foreach ($ticket in @($FirstTicket, $SecondTicket)) {
            $window = Get-DashPresentationOwnedWindow $ticket
            if (!$window.Visible -or $window.Minimized -or $window.Client.Right - $window.Client.Left -ne $ExpectedWidth -or
                $window.Client.Bottom - $window.Client.Top -ne $ExpectedHeight) { throw 'Player window is hidden/minimized or its requested client dimensions changed.' }
            $observations += [pscustomobject]@{
                process_id = $ticket.process_id; start_time_utc_ticks = $ticket.start_time_utc_ticks; executable = $ticket.executable
                window_handle = $window.Handle.ToInt64(); class_name = $window.ClassName; visible = $window.Visible; minimized = $window.Minimized
                client_width = $window.Client.Right - $window.Client.Left; client_height = $window.Client.Bottom - $window.Client.Top
                outer = $window.Outer; overlap_pixels_with_peer = $placement.overlap_pixels
                limitation = 'OS visibility and pair placement only; inspect actual screenshot content separately. Unrelated windows are never controlled.'
            }
        }
        return $observations
    }
    finally { [void][PocketBumpDiagnostics.PresentationWindows]::SetThreadDpiAwarenessContext($previousDpi) }
}
