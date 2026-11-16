using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SquareSnap.Core.Logging;

namespace SquareSnap.Capture.Windows;

/// <summary>
/// Puts the capture on the Windows clipboard twice: as a DIB (what Paint, Word and most apps read) and as "PNG"
/// (what browsers, Discord and Office prefer). The data stays on the clipboard after SquareSnap exits.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe class WindowsClipboard(Logger log) : IClipboardWriter
{
    public bool SetImage(ScreenImage image, byte[] png)
    {
        // The clipboard needs an owner window: with no window, SetClipboardData fails after EmptyClipboard.
        IntPtr owner = Native.CreateWindowEx(0, "STATIC", "SquareSnap clipboard", 0, 0, 0, 0, 0, Native.HWND_MESSAGE, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        if (owner == IntPtr.Zero)
        {
            log.Error($"Couldn't create the clipboard helper window (error {Marshal.GetLastPInvokeError()}); the capture was not copied.");
            return false;
        }
        try
        {
            // Another program may hold the clipboard for a moment; retry for about half a second.
            bool open = false;
            for (int attempt = 0; attempt < 10 && !(open = Native.OpenClipboard(owner)); attempt++) Thread.Sleep(50);
            if (!open)
            {
                log.Warning("The clipboard is busy (another program has it open); the capture was not copied. Try again.");
                return false;
            }
            try
            {
                if (!Native.EmptyClipboard())
                {
                    log.Warning($"Couldn't clear the clipboard (error {Marshal.GetLastPInvokeError()}); the capture was not copied.");
                    return false;
                }
                bool dib = Put(Native.CF_DIB, BuildDib(image), "DIB");
                uint pngFormat = Native.RegisterClipboardFormat("PNG");
                bool pngOk = pngFormat != 0 && Put(pngFormat, png, "PNG");
                log.Debug($"Clipboard: {image.Width}x{image.Height} copied (DIB {(dib ? "ok" : "failed")}, PNG {(pngOk ? "ok" : "failed")}).");
                return dib || pngOk;
            }
            finally
            {
                Native.CloseClipboard();
            }
        }
        finally
        {
            Native.DestroyWindow(owner);
        }
    }

    private bool Put(uint format, byte[] data, string label)
    {
        IntPtr mem = Native.GlobalAlloc(Native.GMEM_MOVEABLE, (nuint)data.Length);
        if (mem == IntPtr.Zero) { log.Warning($"Out of memory copying the {label} clipboard data."); return false; }
        IntPtr p = Native.GlobalLock(mem);
        Marshal.Copy(data, 0, p, data.Length);
        Native.GlobalUnlock(mem);
        if (Native.SetClipboardData(format, mem) == IntPtr.Zero)
        {
            log.Warning($"Windows refused the {label} clipboard data (error {Marshal.GetLastPInvokeError()}).");
            Native.GlobalFree(mem); // only ours to free when the clipboard didn't take it
            return false;
        }
        return true;
    }

    /// <summary>A packed DIB: BITMAPINFOHEADER then bottom-up 32-bit rows.</summary>
    internal static byte[] BuildDib(ScreenImage image)
    {
        int headerSize = sizeof(Native.BITMAPINFOHEADER);
        var result = new byte[headerSize + image.Bgra.Length];
        var header = new Native.BITMAPINFOHEADER
        {
            biSize = (uint)headerSize,
            biWidth = image.Width,
            biHeight = image.Height, // positive = bottom-up, the form older apps expect
            biPlanes = 1,
            biBitCount = 32,
            biCompression = Native.BI_RGB,
            biSizeImage = (uint)image.Bgra.Length,
        };
        MemoryMarshal.Write(result, in header);
        int row = image.Stride;
        for (int y = 0; y < image.Height; y++)
            Buffer.BlockCopy(image.Bgra, y * row, result, headerSize + (image.Height - 1 - y) * row, row);
        return result;
    }
}
