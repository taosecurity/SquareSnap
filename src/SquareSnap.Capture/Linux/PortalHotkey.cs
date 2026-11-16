using SquareSnap.Core.Cli;
using SquareSnap.Core.Logging;
using SquareSnap.Core.Platform;
using Tmds.DBus.Protocol;

namespace SquareSnap.Capture.Linux;

/// <summary>
/// The global hotkey on Linux through the GlobalShortcuts portal (KDE Plasma 6 has it; GNOME 48 and later too).
/// SquareSnap asks for one shortcut, "capture", with the saved hotkey as the preferred keys. The desktop decides:
/// KDE Plasma may show a dialog to confirm it the first time, and the keys can be changed later in
/// System Settings > Keyboard > Shortcuts > SquareSnap. Everything is asynchronous, so progress and problems
/// arrive through <see cref="StatusChanged"/> and the log.
/// </summary>
public sealed class PortalHotkey(Logger log) : IGlobalHotkey
{
    private const string Interface = "org.freedesktop.portal.GlobalShortcuts";
    public const string ShortcutId = "capture";

    private readonly SemaphoreSlim _gate = new(1, 1);
    private string? _session;
    private IDisposable? _activated;
    private int _generation;
    private bool _disposed;

    public event Action? Pressed;

    /// <summary>A message for the status bar (text, is it a problem). Raised on a background thread.</summary>
    public event Action<string, bool>? StatusChanged;

    public string? Register(HotkeySpec? spec)
    {
        int generation = Interlocked.Increment(ref _generation);
        _ = Task.Run(() => RegisterAsync(spec, generation));
        return null; // the answer comes later, through StatusChanged
    }

    private async Task RegisterAsync(HotkeySpec? spec, int generation)
    {
        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (generation != _generation || _disposed) return; // a newer choice replaced this one
            var portal = await Portal.GetAsync(log).ConfigureAwait(false);
            await CloseSessionAsync(portal).ConfigureAwait(false);
            if (spec == null)
            {
                log.Info("Hotkey: off (the GlobalShortcuts session is closed).");
                return;
            }

            // Before install.sh, Plasma doesn't know the app id, and a shortcut asked for then gets no name: a stray
            // launcher entry and no working hotkey (beta 13's test, 2026-11-13). Wait for the install instead.
            if (portal.HostApp == HostAppStatus.NotInstalled)
            {
                Report(LinuxPlan.InstallForHotkey, true);
                return;
            }

            // A shortcut session per choice: the portal binds shortcuts once per session.
            string trigger = spec.ToPortalTrigger();
            log.Info($"Hotkey: asking the GlobalShortcuts portal for {spec} (preferred trigger {trigger}).");
            var created = await portal.RequestAsync(Interface, "CreateSession", "a{sv}",
                static (ref MessageWriter w, string token) => Portal.WriteOptions(ref w, token, [("session_handle_token", Portal.NewToken())]),
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            string? session = created.Code == 0 ? Portal.StringResult(created.Results, "session_handle") : null;
            if (session == null)
            {
                Report($"The desktop didn't open a shortcut session (answer {created.CodeText}). Bind 'squaresnap capture' to a key in your desktop's keyboard settings instead.", true);
                return;
            }
            _session = session;
            log.Info($"Hotkey: shortcut session {session}.");

            _activated = await portal.Connection.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = Interface, Member = "Activated", Path = Portal.ObjectPath },
                static (Message m, object? _) =>
                {
                    var r = m.GetBodyReader();
                    string sessionHandle = r.ReadObjectPathAsString();
                    string id = r.ReadString();
                    return (sessionHandle, id);
                },
                (Notification<(string Session, string Id)> n) =>
                {
                    if (!n.HasValue) { log.Info($"Hotkey: stopped listening ({(n.IsCompletion ? n.Exception?.Message ?? "closed" : "no value")})."); return; }
                    var value = n.Value;
                    if (value.Session != _session || value.Id != ShortcutId) return;
                    log.Info("Hotkey: pressed (GlobalShortcuts portal).");
                    Pressed?.Invoke();
                },
                false, ObserverFlags.None, null).ConfigureAwait(false);

            // Always bind. KDE's portal only reads the app's remembered shortcuts at CreateSession; they become active
            // only through BindShortcuts (xdg-desktop-portal-kde commit 0d743a7). Beta 15 skipped the bind for a known
            // shortcut and the key did nothing on CachyOS (2026-11-14). For a known id current Plasma answers at once,
            // with no dialog; older Plasma (Debian 13's) may show the dialog again. The list is only logged.
            await ListAsync(portal, session).ConfigureAwait(false);

            var bound = await portal.RequestAsync(Interface, "BindShortcuts", "oa(sa{sv})sa{sv}",
                (ref MessageWriter w, string token) =>
                {
                    w.WriteObjectPath(session);
                    var list = w.WriteArrayStart(DBusType.Struct);
                    w.WriteStructureStart();
                    w.WriteString(ShortcutId);
                    var props = w.WriteDictionaryStart();
                    w.WriteDictionaryEntryStart();
                    w.WriteString("description");
                    w.WriteVariantString("Capture a square screenshot");
                    w.WriteDictionaryEntryStart();
                    w.WriteString("preferred_trigger");
                    w.WriteVariantString(trigger);
                    w.WriteDictionaryEnd(props);
                    w.WriteArrayEnd(list);
                    w.WriteString(""); // no parent window
                    Portal.WriteOptions(ref w, token);
                },
                TimeSpan.FromMinutes(2)).ConfigureAwait(false); // KDE may show a confirmation dialog
            if (bound.Code != 0)
            {
                Report($"The desktop didn't bind the hotkey (answer {bound.CodeText}). You can set it in System Settings > Keyboard > Shortcuts, " +
                       "or bind 'squaresnap capture' to a key there.", true);
                return;
            }
            var shortcuts = ParseShortcuts(bound.Results);
            log.Info($"Hotkey: bound. The desktop reports {Describe(shortcuts)}.");
            Finish(shortcuts);
        }
        catch (DBusErrorReplyException ex)
        {
            Report($"This desktop has no global shortcuts portal ({ex.ErrorName}). Bind 'squaresnap capture' to a key in your desktop's keyboard settings instead.", true);
        }
        catch (Exception ex) when (ex is CaptureException or TimeoutException or DBusExceptionBase)
        {
            Report($"The hotkey couldn't be set ({ex.Message}). Bind 'squaresnap capture' to a key in your desktop's keyboard settings instead.", true);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task CloseSessionAsync(Portal portal)
    {
        _activated?.Dispose();
        _activated = null;
        if (_session == null) return;
        string session = _session;
        _session = null;
        try
        {
            var call = portal.Build(session, "org.freedesktop.portal.Session", "Close", "", static (ref MessageWriter _, string _) => { }, "");
            await portal.Connection.CallMethodAsync(call).ConfigureAwait(false);
            log.Info($"Hotkey: closed the shortcut session {session}.");
        }
        catch (DBusExceptionBase ex)
        {
            log.Info($"Hotkey: closing the old shortcut session failed ({ex.Message}); carrying on.");
        }
    }

    /// <summary>The shortcuts the desktop already has for this session, or null when it can't say.</summary>
    private async Task<IReadOnlyList<PortalShortcut>?> ListAsync(Portal portal, string session)
    {
        try
        {
            var listed = await portal.RequestAsync(Interface, "ListShortcuts", "oa{sv}",
                (ref MessageWriter w, string token) =>
                {
                    w.WriteObjectPath(session);
                    Portal.WriteOptions(ref w, token);
                },
                TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            if (listed.Code != 0)
            {
                log.Info($"Hotkey: listing the desktop's shortcuts answered {listed.CodeText}.");
                return null;
            }
            var shortcuts = ParseShortcuts(listed.Results);
            log.Info($"Hotkey: before binding, the desktop remembers {Describe(shortcuts)}.");
            return shortcuts;
        }
        catch (Exception ex) when (ex is DBusExceptionBase or TimeoutException)
        {
            log.Info($"Hotkey: couldn't list the desktop's shortcuts ({ex.Message}).");
            return null;
        }
    }

    private void Finish(IReadOnlyList<PortalShortcut> shortcuts)
    {
        var (message, problem) = LinuxPlan.ShortcutOutcome(shortcuts, ShortcutId);
        if (!problem) log.Info($"Hotkey: {message}");
        Report(message, problem);
    }

    private static string Describe(IReadOnlyList<PortalShortcut> shortcuts) =>
        shortcuts.Count == 0 ? "no shortcuts"
            : string.Join(", ", shortcuts.Select(s => $"'{s.Id}' = {(string.IsNullOrWhiteSpace(s.Trigger) ? "(no key)" : s.Trigger)}"));

    /// <summary>
    /// The "shortcuts" result of BindShortcuts and ListShortcuts: a(sa{sv}), each an id with properties, of which
    /// "trigger_description" is the key as the desktop shows it (empty when none is set).
    /// </summary>
    internal static List<PortalShortcut> ParseShortcuts(Dictionary<string, VariantValue> results)
    {
        var list = new List<PortalShortcut>();
        if (!results.TryGetValue("shortcuts", out var v)) return list;
        v = Unwrap(v);
        if (v.Type != VariantValueType.Array) return list;
        for (int i = 0; i < v.Count; i++)
        {
            var item = Unwrap(v.GetItem(i));
            if (item.Type != VariantValueType.Struct || item.Count < 2) continue;
            var idValue = Unwrap(item.GetItem(0));
            if (idValue.Type != VariantValueType.String) continue;
            string? trigger = null;
            var props = Unwrap(item.GetItem(1));
            if (props.Type == VariantValueType.Dictionary)
            {
                for (int j = 0; j < props.Count; j++)
                {
                    var entry = props.GetDictionaryEntry(j);
                    var key = Unwrap(entry.Key);
                    if (key.Type != VariantValueType.String || key.GetString() != "trigger_description") continue;
                    var value = Unwrap(entry.Value);
                    trigger = value.Type == VariantValueType.String ? value.GetString() : value.ToString();
                }
            }
            list.Add(new PortalShortcut(idValue.GetString(), trigger));
        }
        return list;
    }

    private static VariantValue Unwrap(VariantValue v) => v.Type == VariantValueType.Variant ? v.GetVariantValue() : v;

    private void Report(string message, bool problem)
    {
        if (problem) log.Warning($"Hotkey: {message}");
        StatusChanged?.Invoke(message, problem);
    }

    public void Dispose()
    {
        _disposed = true;
        _activated?.Dispose();
    }
}
