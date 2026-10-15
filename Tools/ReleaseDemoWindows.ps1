# Ordinary Windows mouse/keyboard/screenshot support. No game internals are called.
# Always pair with the PID/start-time/executable checks in PlayerPresentationWindows.
function Initialize-ReleaseDemoInputApi {
    if ('PocketBumpRelease.DesktopInput' -as [type]) { return }
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -TypeDefinition @'
using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
namespace PocketBumpRelease {
    [StructLayout(LayoutKind.Sequential)] public struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int Left,Top,Right,Bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int Size; public Rect Monitor,Work; public uint Flags; }
    [StructLayout(LayoutKind.Sequential)] struct MouseInput { public int X,Y; public uint Data,Flags,Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Sequential)] struct KeyboardInput { public ushort Key,Scan; public uint Flags,Time; public IntPtr Extra; }
    [StructLayout(LayoutKind.Explicit)] struct InputUnion { [FieldOffset(0)] public MouseInput Mouse; [FieldOffset(0)] public KeyboardInput Key; }
    [StructLayout(LayoutKind.Sequential)] struct Input { public uint Type; public InputUnion Data; }
    public static class DesktopInput {
        [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr window);
        [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr window);
        [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr window,out uint processId);
        [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
        [DllImport("user32.dll")] static extern bool AttachThreadInput(uint first,uint second,bool attach);
        [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out Rect rect);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window,out Rect rect);
        [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")] static extern int GetWindowLong(IntPtr window,int index);
        [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
        [DllImport("user32.dll")] static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
        [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref Point point);
        [DllImport("user32.dll",SetLastError=true)] static extern uint SendInput(uint count,Input[] inputs,int size);
        static void Send(Input input) {
            if (SendInput(1,new [] { input },Marshal.SizeOf(typeof(Input))) != 1) throw new Win32Exception();
        }
        public static bool Focus(IntPtr window) {
            if (GetForegroundWindow()==window) return true;
            uint owner;
            uint foreground=GetWindowThreadProcessId(GetForegroundWindow(),out owner);
            uint target=GetWindowThreadProcessId(window,out owner);
            uint current=GetCurrentThreadId();
            bool attached=foreground!=0 && foreground!=current && AttachThreadInput(current,foreground,true);
            bool targetAttached=target!=0 && target!=current && target!=foreground && AttachThreadInput(current,target,true);
            try { BringWindowToTop(window); SetForegroundWindow(window); Thread.Sleep(80); }
            finally {
                if (targetAttached) AttachThreadInput(current,target,false);
                if (attached) AttachThreadInput(current,foreground,false);
            }
            return GetForegroundWindow()==window;
        }
        public static bool IsBorderless(IntPtr window) { return (GetWindowLong(window,-16) & 0x00C40000)==0; }
        static bool TryOwnedCaptionPoint(IntPtr window,uint expectedProcess,out Point point) {
            point=new Point();
            uint owner; GetWindowThreadProcessId(window,out owner);
            if (owner!=expectedProcess || !IsWindowVisible(window) || IsIconic(window)) return false;
            Rect outer; Point client=new Point();
            if (!GetWindowRect(window,out outer) || !ClientToScreen(window,ref client)) return false;
            var monitor=new MonitorInfo(); monitor.Size=Marshal.SizeOf(typeof(MonitorInfo));
            if (!GetMonitorInfo(MonitorFromWindow(window,2),ref monitor) ||
                outer.Left<monitor.Work.Left || outer.Top<monitor.Work.Top ||
                outer.Right>monitor.Work.Right || outer.Bottom>monitor.Work.Bottom) return false;
            int captionHeight=client.Y-outer.Top;
            if (captionHeight<12 || captionHeight>80) return false;
            point=new Point { X=outer.Left+100,Y=outer.Top+captionHeight/2 };
            return point.X<outer.Right-100 && WindowFromPoint(point)==window;
        }
        public static bool ActivateOwnedCaption(IntPtr window,uint expectedProcess) {
            // This verified title-bar action is solely for acquiring focus. Ordinary
            // client clicks below require foreground ownership before button-down.
            Point point;
            if (!TryOwnedCaptionPoint(window,expectedProcess,out point) || !SetCursorPos(point.X,point.Y)) return false;
            Thread.Sleep(60);
            Point checkedPoint,cursor;
            if (!TryOwnedCaptionPoint(window,expectedProcess,out checkedPoint) || !GetCursorPos(out cursor) ||
                cursor.X!=checkedPoint.X || cursor.Y!=checkedPoint.Y) return false;
            bool pressed=false;
            try {
                Send(new Input { Type=0,Data=new InputUnion { Mouse=new MouseInput { Flags=2 } } });
                pressed=true; Thread.Sleep(90);
            }
            finally {
                if (pressed) Send(new Input { Type=0,Data=new InputUnion { Mouse=new MouseInput { Flags=4 } } });
            }
            Thread.Sleep(150);
            return GetForegroundWindow()==window;
        }
        public static void Key(ushort key,bool down) {
            Send(new Input { Type=1, Data=new InputUnion { Key=new KeyboardInput { Key=key, Flags=down?0u:2u } } });
        }
        public static void Text(string text) {
            foreach (char value in text) {
                Send(new Input { Type=1, Data=new InputUnion { Key=new KeyboardInput { Scan=value,Flags=4 } } });
                Send(new Input { Type=1, Data=new InputUnion { Key=new KeyboardInput { Scan=value,Flags=6 } } });
                Thread.Sleep(25);
            }
        }
        static Point RequireOwnedClientTarget(IntPtr window,uint expectedProcess,int x,int y) {
            uint owner; GetWindowThreadProcessId(window,out owner);
            if (owner!=expectedProcess || GetForegroundWindow()!=window || !IsWindowVisible(window) || IsIconic(window))
                throw new InvalidOperationException("Owned click target is not the visible foreground Player.");
            Rect client;
            if (!GetClientRect(window,out client)) throw new Win32Exception();
            if (x<client.Left || x>=client.Right || y<client.Top || y>=client.Bottom)
                throw new InvalidOperationException("Click target is outside the owned client rectangle.");
            Point point=new Point { X=x,Y=y };
            if (!ClientToScreen(window,ref point)) throw new Win32Exception();
            IntPtr hit=WindowFromPoint(point);
            uint hitOwner; GetWindowThreadProcessId(hit,out hitOwner);
            if (hit==IntPtr.Zero || hitOwner!=expectedProcess || GetAncestor(hit,2)!=window)
                throw new InvalidOperationException("Click point is occluded or does not hit the owned Player.");
            return point;
        }
        static void RequireCursor(Point expected,string phase) {
            Point cursor;
            if (!GetCursorPos(out cursor)) throw new InvalidOperationException("Cannot read cursor during "+phase+"; no further click will be sent.");
            if (cursor.X!=expected.X || cursor.Y!=expected.Y)
                throw new InvalidOperationException("Cursor moved during "+phase+": expected ("+expected.X+","+expected.Y+") actual ("+cursor.X+","+cursor.Y+"); no further click will be sent.");
        }
        public static void Click(IntPtr window,uint expectedProcess,int x,int y) {
            Point point=RequireOwnedClientTarget(window,expectedProcess,x,y);
            if (!SetCursorPos(point.X,point.Y)) throw new Win32Exception();
            Thread.Sleep(80);
            RequireCursor(RequireOwnedClientTarget(window,expectedProcess,x,y),"before button-down");
            bool pressed=false;
            try {
                Send(new Input { Type=0,Data=new InputUnion { Mouse=new MouseInput { Flags=2 } } });
                pressed=true;
                Thread.Sleep(100);
                RequireCursor(RequireOwnedClientTarget(window,expectedProcess,x,y),"button held");
            }
            finally {
                // Always release our own button-down, even if focus/occlusion changes
                // during the press. Never leave a held mouse button on the desktop.
                if (pressed) Send(new Input { Type=0,Data=new InputUnion { Mouse=new MouseInput { Flags=4 } } });
            }
        }
        public static Point ClientOrigin(IntPtr window) {
            Point point=new Point();
            if (!ClientToScreen(window,ref point)) throw new Win32Exception();
            return point;
        }
    }
}
'@ -ErrorAction Stop
}

function Test-ReleaseDemoRectangleContained($Inner, $Outer) {
    return [long]$Inner.Right -gt [long]$Inner.Left -and [long]$Inner.Bottom -gt [long]$Inner.Top -and
        [long]$Inner.Left -ge [long]$Outer.Left -and [long]$Inner.Top -ge [long]$Outer.Top -and
        [long]$Inner.Right -le [long]$Outer.Right -and [long]$Inner.Bottom -le [long]$Outer.Bottom
}

function Get-ReleaseDemoWindowPlacement {
    param([Parameter(Mandatory = $true)]$Outer,
        [Parameter(Mandatory = $true)][AllowEmptyCollection()][object[]]$WorkAreas,
        [object[]]$MonitorBounds = @(), [switch]$Borderless)
    [long]$width = [long]$Outer.Right - [long]$Outer.Left
    [long]$height = [long]$Outer.Bottom - [long]$Outer.Top
    if ($width -le 0 -or $height -le 0) { throw 'Owned window dimensions must be positive.' }
    foreach ($area in $WorkAreas) {
        if (Test-ReleaseDemoRectangleContained $Outer $area) {
            return [pscustomobject]@{ x=[int]$Outer.Left; y=[int]$Outer.Top; move=$false; region='work_area' }
        }
    }
    # Fullscreen borderless legitimately covers a monitor's taskbar work-area inset.
    # Only an exact monitor rectangle qualifies; partial off-screen windows do not.
    if ($Borderless) {
        foreach ($monitor in $MonitorBounds) {
            if ($Outer.Left -eq $monitor.Left -and $Outer.Top -eq $monitor.Top -and
                $Outer.Right -eq $monitor.Right -and $Outer.Bottom -eq $monitor.Bottom) {
                return [pscustomobject]@{ x=[int]$Outer.Left; y=[int]$Outer.Top; move=$false; region='fullscreen_monitor' }
            }
        }
    }
    $best = $null; $bestDistance = [double]::PositiveInfinity
    foreach ($area in $WorkAreas) {
        if ($width -gt [long]$area.Right - [long]$area.Left -or $height -gt [long]$area.Bottom - [long]$area.Top) { continue }
        [long]$x = [Math]::Max([long]$area.Left, [Math]::Min([long]$Outer.Left, [long]$area.Right - $width))
        [long]$y = [Math]::Max([long]$area.Top, [Math]::Min([long]$Outer.Top, [long]$area.Bottom - $height))
        $distance = [Math]::Pow([double]($x - $Outer.Left), 2) + [Math]::Pow([double]($y - $Outer.Top), 2)
        if ($distance -lt $bestDistance) {
            $bestDistance = $distance
            $best = [pscustomobject]@{ x=[int]$x; y=[int]$y; move=$true; region='work_area' }
        }
    }
    if ($null -eq $best) { throw "No monitor work area fits the existing owned window dimensions ${width}x${height}; resizing is not permitted." }
    return $best
}

function Move-ReleaseDemoOwnedWindowIntoView($Ticket, $Window) {
    $placement = Get-ReleaseDemoWindowPlacement -Outer $Window.Outer `
        -WorkAreas ([PocketBumpDiagnostics.PresentationWindows]::WorkAreas()) `
        -MonitorBounds @([Windows.Forms.Screen]::AllScreens | ForEach-Object { $_.Bounds }) `
        -Borderless:([PocketBumpRelease.DesktopInput]::IsBorderless($Window.Handle))
    if (!$placement.move) { return $Window }
    Assert-DashPresentationWindowIdentity $Ticket
    $current = Get-DashPresentationOwnedWindow $Ticket
    if ($current.Handle -ne $Window.Handle -or $current.Outer.Left -ne $Window.Outer.Left -or
        $current.Outer.Top -ne $Window.Outer.Top -or $current.Outer.Right -ne $Window.Outer.Right -or
        $current.Outer.Bottom -ne $Window.Outer.Bottom) { throw 'Owned window geometry changed before repositioning.' }
    # SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE: change only this verified HWND's position.
    if (![PocketBumpDiagnostics.PresentationWindows]::SetWindowPos($current.Handle, [IntPtr]::Zero,
            $placement.x, $placement.y, 0, 0, 0x0015)) { throw 'Could not move the owned Player fully into its monitor work area.' }
    $moved = Get-DashPresentationOwnedWindow $Ticket
    if ($moved.Outer.Left -ne $placement.x -or $moved.Outer.Top -ne $placement.y -or
        ($moved.Outer.Right - $moved.Outer.Left) -ne ($Window.Outer.Right - $Window.Outer.Left) -or
        ($moved.Outer.Bottom - $moved.Outer.Top) -ne ($Window.Outer.Bottom - $Window.Outer.Top) -or
        ($moved.Client.Right - $moved.Client.Left) -ne ($Window.Client.Right - $Window.Client.Left) -or
        ($moved.Client.Bottom - $moved.Client.Top) -ne ($Window.Client.Bottom - $Window.Client.Top)) {
        throw 'Owned window did not retain its original dimensions at the verified on-screen position.'
    }
    return $moved
}

function Save-ReleaseDemoClientPixels($Window, [string]$Path) {
    $origin = [PocketBumpRelease.DesktopInput]::ClientOrigin($Window.Handle)
    $width = $Window.Client.Right - $Window.Client.Left
    $height = $Window.Client.Bottom - $Window.Client.Top
    if ($width -lt 100 -or $height -lt 100) { throw 'Invalid Player client dimensions.' }
    $bitmap = New-Object Drawing.Bitmap $width, $height
    try {
        $graphics = [Drawing.Graphics]::FromImage($bitmap)
        try { $graphics.CopyFromScreen($origin.X, $origin.Y, 0, 0, (New-Object Drawing.Size $width, $height)) }
        finally { $graphics.Dispose() }
        $bitmap.Save($Path, [Drawing.Imaging.ImageFormat]::Png)
        $visible = 0
        for ($y = 0; $y -lt $height; $y += 20) {
            for ($x = 0; $x -lt $width; $x += 20) {
                $pixel = $bitmap.GetPixel($x, $y)
                if ($pixel.R + $pixel.G + $pixel.B -gt 60) { $visible++ }
            }
        }
        return $visible
    }
    finally { $bitmap.Dispose() }
}

function Get-ReleaseDemoForegroundIdentity([IntPtr]$Foreground) {
    # Only process identity, never another application's title, path or contents.
    [uint32]$foregroundProcessId = 0
    $null = [PocketBumpRelease.DesktopInput]::GetWindowThreadProcessId($Foreground, [ref]$foregroundProcessId)
    $name = 'unavailable'
    if ($foregroundProcessId -ne 0) {
        try { $name = (Get-Process -Id $foregroundProcessId -ErrorAction Stop).ProcessName }
        catch { } # The foreground process can exit between these read-only calls.
    }
    return "foreground_hwnd=$($Foreground.ToInt64()) foreground_pid=$foregroundProcessId foreground_process=$name"
}

function Focus-ReleaseDemoWindow($Ticket) {
    $observations = @()
    for ($attempt = 1; $attempt -le 3; $attempt++) {
        $window = Get-DashPresentationOwnedWindow $Ticket
        if ($null -eq $window) { throw 'The owned Player has no identifiable Unity window.' }
        $Ticket.window_handle = $window.Handle
        if (!$window.Visible -or $window.Minimized) {
            # Visible-window testing is explicitly opt-in. Restore only the verified
            # Player created by this run, never another application's window.
            Assert-DashPresentationWindowIdentity $Ticket
            Write-Host "Release restoring owned window: attempt=$attempt owned_pid=$($Ticket.process_id) visible=$($window.Visible) minimized=$($window.Minimized)"
            $null = [PocketBumpDiagnostics.PresentationWindows]::ShowWindow($window.Handle, 9)
            Start-Sleep -Milliseconds 250
            $window = Get-DashPresentationOwnedWindow $Ticket
            if (!$window.Visible -or $window.Minimized) {
                $observations += "attempt=$attempt owned window remained hidden/minimized after restore"
                continue
            }
        }
        $window = Move-ReleaseDemoOwnedWindowIntoView $Ticket $window
        $focused = [PocketBumpRelease.DesktopInput]::Focus($window.Handle)
        if (!$focused) {
            Assert-DashPresentationWindowIdentity $Ticket
            # Raise only this owned HWND without activating, moving or resizing it.
            # The caption fallback independently verifies full work-area containment
            # and an exact owned-HWND hit immediately before its activation click.
            $raised = [PocketBumpDiagnostics.PresentationWindows]::SetWindowPos($window.Handle, [IntPtr]::Zero, 0, 0, 0, 0, 0x0053)
            if ($raised) {
                Start-Sleep -Milliseconds 120
                $focused = [PocketBumpRelease.DesktopInput]::ActivateOwnedCaption($window.Handle, [uint32]$Ticket.process_id)
            }
        }
        # A transient successful activation is not enough. Keep stability validation
        # inside the retry boundary; retries never send gameplay keys or UI clicks.
        $observedForeground = [PocketBumpRelease.DesktopInput]::GetForegroundWindow()
        for ($sample = 0; $focused -and $sample -lt 3; $sample++) {
            Start-Sleep -Milliseconds 80
            Assert-DashPresentationWindowIdentity $Ticket
            $current = Get-DashPresentationOwnedWindow $Ticket
            $observedForeground = [PocketBumpRelease.DesktopInput]::GetForegroundWindow()
            $focused = $null -ne $current -and $current.Visible -and !$current.Minimized -and
                $current.Handle -eq $window.Handle -and $observedForeground -eq $window.Handle
        }
        if ($focused) { return $current }
        $observation = "attempt=$attempt owned_hwnd=$($window.Handle.ToInt64()) owned_pid=$($Ticket.process_id) $(Get-ReleaseDemoForegroundIdentity $observedForeground)"
        $observations += $observation
        Write-Host "Release activation retry: $observation"
        if ($attempt -lt 3) { Start-Sleep -Milliseconds 150 }
    }
    throw ('Could not keep the owned Player foreground after three bounded activation attempts. No gameplay input was sent. ' + ($observations -join '; '))
}

function Send-ReleaseDemoKey($Ticket, [uint16]$Key, [int]$HoldMilliseconds = 120) {
    $null = Focus-ReleaseDemoWindow $Ticket
    try {
        [PocketBumpRelease.DesktopInput]::Key($Key, $true)
        Start-Sleep -Milliseconds $HoldMilliseconds
    }
    finally { [PocketBumpRelease.DesktopInput]::Key($Key, $false) }
    Start-Sleep -Milliseconds 180
}

function Click-ReleaseDemoUi($Ticket, [double]$AnchorX, [double]$AnchorY,
    [double]$OffsetX = 0, [double]$OffsetY = 0, [double]$ReferenceWidth = 1920, [double]$ReferenceHeight = 1080) {
    $window = Focus-ReleaseDemoWindow $Ticket
    $width = $window.Client.Right - $window.Client.Left
    $height = $window.Client.Bottom - $window.Client.Top
    $scale = [Math]::Sqrt(($width / $ReferenceWidth) * ($height / $ReferenceHeight))
    $x = [int][Math]::Round($width * $AnchorX + $OffsetX * $scale)
    $y = [int][Math]::Round($height * (1 - $AnchorY) - $OffsetY * $scale)
    if ($x -le 0 -or $y -le 0 -or $x -ge $width -or $y -ge $height) { throw 'UI target is outside the owned client rectangle.' }
    Assert-DashPresentationWindowIdentity $Ticket
    [PocketBumpRelease.DesktopInput]::Click($window.Handle, [uint32]$Ticket.process_id, $x, $y)
    Start-Sleep -Milliseconds 500
}

function Set-ReleaseDemoAddress($Ticket) {
    Click-ReleaseDemoUi $Ticket .5 .42 0 -28.8
    $null = Focus-ReleaseDemoWindow $Ticket
    try {
        [PocketBumpRelease.DesktopInput]::Key(0x11, $true)
        [PocketBumpRelease.DesktopInput]::Key(0x41, $true)
        Start-Sleep -Milliseconds 80
    }
    finally {
        [PocketBumpRelease.DesktopInput]::Key(0x41, $false)
        [PocketBumpRelease.DesktopInput]::Key(0x11, $false)
    }
    [PocketBumpRelease.DesktopInput]::Text('127.0.0.1')
    Start-Sleep -Milliseconds 200
}
