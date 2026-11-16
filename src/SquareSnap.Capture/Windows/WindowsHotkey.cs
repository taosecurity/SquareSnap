using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using SquareSnap.Core.Cli;
using SquareSnap.Core.Logging;

namespace SquareSnap.Capture.Windows;

/// <summary>
/// A global hotkey via RegisterHotKey on a small background thread with its own message loop, so it doesn't
/// depend on the UI toolkit's window procedure. Re-registering stops the old thread and starts a new one.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed class WindowsHotkey(Logger log) : IGlobalHotkey
{
    private const int HotkeyId = 0x5353; // "SS"
    private Thread? _thread;
    private uint _threadId;

    public event Action? Pressed;

    public string? Register(HotkeySpec? spec)
    {
        Stop();
        if (spec == null)
        {
            log.Info("Global hotkey: off.");
            return null;
        }

        uint vk = VirtualKey(spec.Key);
        uint mods = (uint)spec.Modifiers | Native.MOD_NOREPEAT; // HotkeyModifiers uses the MOD_* values
        string? error = null;
        using var ready = new ManualResetEventSlim();

        _thread = new Thread(() =>
        {
            _threadId = Native.GetCurrentThreadId();
            // Make sure this thread has a message queue before anyone posts WM_QUIT to it.
            Native.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            if (!Native.RegisterHotKey(IntPtr.Zero, HotkeyId, mods, vk))
            {
                int code = Marshal.GetLastPInvokeError();
                error = code == 1409 // ERROR_HOTKEY_ALREADY_REGISTERED
                    ? $"The hotkey {spec} is already used by another program. Pick another one in Options > Hotkey."
                    : $"Windows refused the hotkey {spec} (error {code}). Pick another one in Options > Hotkey.";
                ready.Set();
                return;
            }
            ready.Set();
            try
            {
                while (Native.GetMessage(out var msg, IntPtr.Zero, 0, 0) > 0)
                {
                    if (msg.message == Native.WM_HOTKEY && (int)msg.wParam == HotkeyId)
                    {
                        log.Debug($"Hotkey {spec} pressed.");
                        try { Pressed?.Invoke(); }
                        catch (Exception ex) { log.Error("The hotkey handler failed", ex); }
                    }
                }
            }
            finally
            {
                Native.UnregisterHotKey(IntPtr.Zero, HotkeyId);
            }
        })
        { IsBackground = true, Name = "SquareSnap hotkey" };
        _thread.Start();
        ready.Wait(TimeSpan.FromSeconds(5));

        if (error != null)
        {
            log.Warning(error);
            _thread = null;
            return error;
        }
        log.Info($"Global hotkey: {spec}.");
        return null;
    }

    public void Dispose() => Stop();

    private void Stop()
    {
        if (_thread == null) return;
        Native.PostThreadMessage(_threadId, Native.WM_QUIT, 0, 0);
        _thread.Join(TimeSpan.FromSeconds(2));
        _thread = null;
    }

    /// <summary>Windows virtual-key code for a key name accepted by <see cref="HotkeySpec"/>.</summary>
    internal static uint VirtualKey(string key)
    {
        if (key.Length == 1) return key[0]; // 'A'-'Z' and '0'-'9' are their own codes
        if (key[0] == 'F' && int.TryParse(key[1..], out int f)) return (uint)(0x70 + f - 1);
        return key switch
        {
            "PrintScreen" => 0x2C,
            "Insert" => 0x2D,
            "Pause" => 0x13,
            "Home" => 0x24,
            "End" => 0x23,
            "PageUp" => 0x21,
            "PageDown" => 0x22,
            _ => throw new ArgumentException($"No virtual-key code for '{key}'.", nameof(key)),
        };
    }
}
