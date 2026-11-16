using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SquareSnap.Core.Geometry;
using SquareSnap.Core.Logging;

namespace SquareSnap.Capture.Windows;

/// <summary>
/// Windows capture with GDI BitBlt, the same mechanism as 1.0's Graphics.CopyFromScreen, but over the whole
/// virtual desktop instead of the primary monitor. Needs a per-monitor DPI-aware process (see
/// <see cref="WindowsDpi.EnsurePerMonitorV2"/>) so every coordinate is a real pixel.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsGdiCapture(Logger log) : ICaptureBackend
{
    public string Name => "Windows GDI";

    [ThreadStatic] private static List<Display>? _found;

    public MonitorLayout GetLayout()
    {
        _found = [];
        try
        {
            if (!Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, &OnMonitor, IntPtr.Zero) || _found.Count == 0)
                throw new CaptureException("Windows reported no monitors. If SquareSnap runs as a service or in a disconnected " +
                                           "remote session, there is no screen to capture: run it from your signed-in desktop.");
            var layout = new MonitorLayout(_found);
            foreach (var m in layout.Monitors) log.Info($"Monitor: {m}");
            log.Info($"Virtual desktop: {layout.VirtualBounds}");
            return layout;
        }
        finally
        {
            _found = null;
        }
    }

    [UnmanagedCallersOnly]
    private static int OnMonitor(IntPtr monitor, IntPtr hdc, Native.RECT* rect, IntPtr data)
    {
        var info = new Native.MONITORINFOEXW { cbSize = (uint)sizeof(Native.MONITORINFOEXW) };
        if (!Native.GetMonitorInfo(monitor, &info)) return 1;
        var r = info.rcMonitor;
        double scale = 1.0;
        if (Native.GetDpiForMonitor(monitor, 0 /* MDT_EFFECTIVE_DPI */, out uint dpiX, out _) == 0 && dpiX > 0)
            scale = dpiX / 96.0;
        string name = new string(info.szDevice).TrimEnd('\0');
        if (name.StartsWith(@"\\.\", StringComparison.Ordinal)) name = name[4..];
        _found?.Add(new Display(name, new PixelRect(r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top), scale,
            (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0));
        return 1; // keep enumerating
    }

    public ScreenImage CaptureAll(MonitorLayout layout)
    {
        var sw = Stopwatch.StartNew();
        var v = layout.VirtualBounds;

        // Cross-check against Windows' own idea of the virtual screen; a mismatch means the process isn't DPI aware.
        var sys = new PixelRect(Native.GetSystemMetrics(Native.SM_XVIRTUALSCREEN), Native.GetSystemMetrics(Native.SM_YVIRTUALSCREEN),
            Native.GetSystemMetrics(Native.SM_CXVIRTUALSCREEN), Native.GetSystemMetrics(Native.SM_CYVIRTUALSCREEN));
        if (sys != v) log.Warning($"Windows reports the virtual screen as {sys} but the monitors cover {v}; using the monitors.");

        IntPtr screen = Native.GetDC(IntPtr.Zero);
        if (screen == IntPtr.Zero)
            throw new CaptureException($"Windows refused access to the screen (GetDC error {Marshal.GetLastPInvokeError()}). " +
                                       "Run SquareSnap from your signed-in desktop, not as a service or scheduled task.");
        IntPtr mem = IntPtr.Zero, dib = IntPtr.Zero, old = IntPtr.Zero;
        try
        {
            mem = Native.CreateCompatibleDC(screen);
            var header = new Native.BITMAPINFOHEADER
            {
                biSize = (uint)sizeof(Native.BITMAPINFOHEADER),
                biWidth = v.Width,
                biHeight = -v.Height, // negative = top row first
                biPlanes = 1,
                biBitCount = 32,
                biCompression = Native.BI_RGB,
            };
            dib = Native.CreateDIBSection(screen, &header, Native.DIB_RGB_COLORS, out IntPtr bits, IntPtr.Zero, 0);
            if (mem == IntPtr.Zero || dib == IntPtr.Zero)
                throw new CaptureException($"Windows couldn't make a {v.Width}x{v.Height} image buffer (error {Marshal.GetLastPInvokeError()}). " +
                                           "Close some programs to free memory and try again.");
            old = Native.SelectObject(mem, dib);

            // One copy per monitor, each covering only that monitor, placed at its spot in the virtual-desktop image.
            // A single copy spanning monitors with different scaling or colour handling makes Windows convert them to
            // one common format, which rounded ~0.15% of the second monitor's pixels by 1 (pixelcheck, betas 8-9;
            // the main monitor matched exactly). 1.0's CopyFromScreen of one region never crossed a monitor.
            // Plain SRCCOPY like CopyFromScreen; CAPTUREBLT made no difference here and makes the cursor flicker.
            foreach (var m in layout.Monitors)
            {
                var b = m.Bounds;
                if (!Native.BitBlt(mem, b.X - v.X, b.Y - v.Y, b.Width, b.Height, screen, b.X, b.Y, Native.SRCCOPY))
                    throw new CaptureException($"Windows refused the screen copy of {m.Name} (BitBlt error {Marshal.GetLastPInvokeError()}). " +
                                               "This happens when the desktop is locked or the session is disconnected.");
            }

            var pixels = new byte[v.Width * v.Height * 4];
            Marshal.Copy(bits, pixels, 0, pixels.Length);
            // GDI leaves the alpha byte at 0; screens are opaque.
            for (int i = 3; i < pixels.Length; i += 4) pixels[i] = 255;

            log.Debug($"Captured {v.Width}x{v.Height} with {Name} in {sw.ElapsedMilliseconds} ms.");
            return new ScreenImage(v.Width, v.Height, v, pixels);
        }
        finally
        {
            if (old != IntPtr.Zero) Native.SelectObject(mem, old);
            if (dib != IntPtr.Zero) Native.DeleteObject(dib);
            if (mem != IntPtr.Zero) Native.DeleteDC(mem);
            Native.ReleaseDC(IntPtr.Zero, screen);
        }
    }
}

/// <summary>DPI awareness and console helpers called from Program.Main before any window exists.</summary>
[SupportedOSPlatform("windows")]
public static class WindowsDpi
{
    /// <summary>
    /// Makes the process per-monitor DPI aware (V2) if the manifest hasn't already. Returns a line for the log.
    /// Without this, Windows scales coordinates on scaled monitors and captures come out shifted or blurred.
    /// </summary>
    public static string EnsurePerMonitorV2()
    {
        bool set = Native.SetProcessDpiAwarenessContext(Native.DpiAwarenessContextPerMonitorAwareV2);
        int awareness = Native.GetAwarenessFromDpiAwarenessContext(Native.GetThreadDpiAwarenessContext());
        string name = awareness switch { 0 => "unaware", 1 => "system aware", 2 => "per-monitor aware", _ => $"unknown ({awareness})" };
        return $"DPI awareness: {name}{(set ? " (set now)" : " (already set by the manifest)")}.";
    }

    /// <summary>
    /// Connects Console output to the terminal that started SquareSnap (it's a windowed app, so it has none of its own).
    /// Output that already has somewhere to go (a file, a pipe, or the terminal handed over by SquareSnap.com) stays
    /// there; only stdout or stderr with nowhere to go is pointed at the parent's terminal. Same approach as BejView.
    /// </summary>
    public static void AttachParentConsoleIfNeeded()
    {
        nint outH = Native.GetStdHandle(Native.STD_OUTPUT_HANDLE), errH = Native.GetStdHandle(Native.STD_ERROR_HANDLE);
        bool outOk = Usable(outH), errOk = Usable(errH);
        if (outOk && errOk) return;
        if (!Native.AttachConsole(Native.ATTACH_PARENT_PROCESS)) return; // started from Explorer: no terminal to borrow
        // Put back the handles that were already redirected, in case attaching replaced them.
        if (outOk) Native.SetStdHandle(Native.STD_OUTPUT_HANDLE, outH);
        if (errOk) Native.SetStdHandle(Native.STD_ERROR_HANDLE, errH);
    }

    private static bool Usable(nint h) => h != 0 && h != -1 && Native.GetFileType(h) != Native.FILE_TYPE_UNKNOWN;
}
