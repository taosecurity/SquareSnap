namespace SquareSnap.Core.Cli;

[Flags]
public enum HotkeyModifiers
{
    None = 0,
    Alt = 1,
    Ctrl = 2,
    Shift = 4,
    Win = 8,
}

/// <summary>
/// A global hotkey such as "Ctrl+Alt+S". The key is a letter, a digit, F1-F24, PrintScreen, Insert, Pause,
/// Home, End, PageUp or PageDown. At least one of Ctrl, Alt or Win is required, so plain typing never triggers it.
/// </summary>
public sealed record HotkeySpec(HotkeyModifiers Modifiers, string Key)
{
    private static readonly HashSet<string> NamedKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        "PrintScreen", "Insert", "Pause", "Home", "End", "PageUp", "PageDown",
    };

    public override string ToString()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("Win");
        parts.Add(Key);
        return string.Join('+', parts);
    }

    /// <summary>
    /// The shortcut in the freedesktop shortcuts format the GlobalShortcuts portal takes as "preferred_trigger":
    /// CTRL, ALT, SHIFT and LOGO joined by '+', then the XKB key name ("CTRL+ALT+s", "LOGO+Print", "CTRL+Prior").
    /// The desktop treats it as a suggestion; on KDE Plasma the user can change it in System Settings > Shortcuts.
    /// </summary>
    public string ToPortalTrigger()
    {
        var parts = new List<string>();
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("CTRL");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("ALT");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("SHIFT");
        if (Modifiers.HasFlag(HotkeyModifiers.Win)) parts.Add("LOGO");
        parts.Add(Key.ToLowerInvariant() switch
        {
            "printscreen" => "Print",
            "pageup" => "Prior",
            "pagedown" => "Next",
            "insert" => "Insert",
            "pause" => "Pause",
            "home" => "Home",
            "end" => "End",
            var k when k.Length == 1 => k,                      // a letter (lower case, as XKB names them) or a digit
            var k when k.StartsWith('f') => "F" + k[1..],        // F1-F24
            _ => Key,
        });
        return string.Join('+', parts);
    }

    /// <summary>Empty text means "no hotkey" and returns true with a null spec.</summary>
    public static bool TryParse(string? text, out HotkeySpec? spec, out string? error)
    {
        spec = null;
        error = null;
        if (string.IsNullOrWhiteSpace(text) || text.Trim().Equals("off", StringComparison.OrdinalIgnoreCase)) return true;

        var mods = HotkeyModifiers.None;
        string? key = null;
        foreach (string raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl" or "control": mods |= HotkeyModifiers.Ctrl; break;
                case "alt": mods |= HotkeyModifiers.Alt; break;
                case "shift": mods |= HotkeyModifiers.Shift; break;
                case "win" or "super" or "meta": mods |= HotkeyModifiers.Win; break;
                case "": error = $"Hotkey '{text}' has an empty part; write it like Ctrl+Alt+S."; return false;
                default:
                    if (key != null) { error = $"Hotkey '{text}' has two keys ({key} and {raw}); use one key plus modifiers."; return false; }
                    key = NormalizeKey(raw);
                    if (key == null) { error = $"'{raw}' isn't a key SquareSnap can use for a hotkey. Use a letter, a digit, F1-F24, PrintScreen, Insert, Pause, Home, End, PageUp or PageDown."; return false; }
                    break;
            }
        }

        if (key == null) { error = $"Hotkey '{text}' has no key; add one, as in Ctrl+Alt+S."; return false; }
        if ((mods & (HotkeyModifiers.Ctrl | HotkeyModifiers.Alt | HotkeyModifiers.Win)) == 0 && !IsFunctionKey(key) && key != "PrintScreen")
        {
            error = $"Hotkey '{text}' needs Ctrl, Alt or Win, or it would fire while you type.";
            return false;
        }
        spec = new HotkeySpec(mods, key);
        return true;
    }

    /// <summary>F1-F24 (the letter F alone is not a function key: Shift+F would fire on every capital F).</summary>
    private static bool IsFunctionKey(string key) =>
        key.Length >= 2 && key[0] == 'F' && int.TryParse(key.AsSpan(1), out int n) && n is >= 1 and <= 24;

    private static string? NormalizeKey(string k)
    {
        if (k.Length == 1 && char.IsAsciiLetterOrDigit(k[0])) return k.ToUpperInvariant();
        if ((k[0] is 'F' or 'f') && int.TryParse(k[1..], out int n) && n is >= 1 and <= 24) return "F" + n;
        if (k.Equals("PrtSc", StringComparison.OrdinalIgnoreCase) || k.Equals("PrintScr", StringComparison.OrdinalIgnoreCase)) return "PrintScreen";
        return NamedKeys.TryGetValue(k, out string? named) ? named : null;
    }
}
