using System.Runtime.Versioning;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Media.Imaging;
using SquareSnap.Capture;
using SquareSnap.Capture.Linux;
using SquareSnap.Core.Logging;

namespace SquareSnap.App;

/// <summary>
/// The window's clipboard on Linux. Avalonia's clipboard when the main window is the active window (Copy, Ctrl+C):
/// Wayland only takes a clipboard change from the program that has the keyboard (Avalonia sends the last input's
/// serial, and KWin ignores it otherwise). Right after a capture the window is hidden or not yet active, so the
/// automatic copy goes through wl-copy (or xclip), which also keeps it after SquareSnap exits. If neither is
/// installed, Avalonia's clipboard is tried anyway.
/// </summary>
[SupportedOSPlatform("linux")]
internal sealed class LinuxWindowClipboard(Logger log, Window window, CommandClipboard command) : IClipboardWriter
{
    private Bitmap? _offered; // Avalonia hands the data out when another program pastes, so it must stay alive

    public bool SetImage(ScreenImage image, byte[] png)
    {
        if (window.IsActive && TryAvalonia(png, "the window is active")) return true;
        if (!window.IsActive) log.Info("Clipboard: the SquareSnap window isn't active, so Wayland wouldn't take Avalonia's copy; using wl-copy or xclip.");
        if (command.SetImage(image, png)) return true;
        if (!window.IsActive)
            log.Warning("Clipboard: Wayland may ignore the next copy because SquareSnap's window isn't active. If pasting gives nothing, " +
                        "click Copy in SquareSnap's window, or install wl-clipboard so captures from the hotkey always reach the clipboard.");
        return TryAvalonia(png, "wl-copy and xclip didn't work");
    }

    private bool TryAvalonia(byte[] png, string why)
    {
        var clipboard = window.Clipboard;
        if (clipboard == null)
        {
            log.Info("Clipboard: Avalonia has no clipboard for this window.");
            return false;
        }
        try
        {
            var bitmap = new Bitmap(new MemoryStream(png));
            var old = _offered;
            _offered = bitmap;
            clipboard.SetBitmapAsync(bitmap).ContinueWith(t =>
            {
                if (t.Exception is { } ex) log.Warning($"Clipboard: Avalonia's clipboard failed ({ex.GetBaseException().Message}).");
            }, TaskScheduler.Default);
            old?.Dispose();
            log.Info($"Clipboard: offered the PNG through Avalonia's clipboard ({why}).");
            return true;
        }
        catch (Exception ex)
        {
            log.Warning($"Clipboard: Avalonia's clipboard failed ({ex.Message}).");
            return false;
        }
    }
}
