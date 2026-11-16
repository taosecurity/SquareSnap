using System.Security.Cryptography;
using SquareSnap.Core.Logging;
using SquareSnap.Core.Platform;
using Tmds.DBus.Protocol;

namespace SquareSnap.Capture.Linux;

/// <summary>A portal request finished: the response code (0 ok, 1 cancelled by the user, 2 other) and its results.</summary>
internal sealed record PortalResponse(uint Code, Dictionary<string, VariantValue> Results)
{
    public string CodeText => Code switch { 0 => "0 (success)", 1 => "1 (cancelled by the user)", _ => $"{Code} (failed)" };
}

/// <summary>Writes a portal method's arguments; <c>token</c> goes into the options as "handle_token".</summary>
internal delegate void PortalBodyWriter(ref MessageWriter writer, string token);

/// <summary>
/// One D-Bus session-bus connection to xdg-desktop-portal, shared by the screenshot and the global shortcuts.
/// Portal calls are "requests": the method returns a request path at once, and the answer comes later as a Response
/// signal on that path (after any dialog the desktop shows). We subscribe to the expected path before calling,
/// as the portal documentation advises, so a fast answer isn't missed.
/// </summary>
internal sealed class Portal : IDisposable
{
    public const string Service = "org.freedesktop.portal.Desktop";
    public const string ObjectPath = "/org/freedesktop/portal/desktop";

    private static readonly SemaphoreSlim Gate = new(1, 1);
    private static Portal? _shared;

    private readonly Logger _log;
    public DBusConnection Connection { get; }

    private Portal(DBusConnection connection, Logger log)
    {
        Connection = connection;
        _log = log;
    }

    /// <summary>The shared connection, made on first use. Throws <see cref="CaptureException"/> with the reason when there's no session bus.</summary>
    public static async Task<Portal> GetAsync(Logger log)
    {
        await Gate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_shared != null) return _shared;
            string? address = DBusAddress.Session;
            if (string.IsNullOrEmpty(address))
                throw new CaptureException("There's no D-Bus session bus (DBUS_SESSION_BUS_ADDRESS isn't set), so the desktop portal can't be reached.");
            var connection = new DBusConnection(address);
            try
            {
                await connection.ConnectAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                connection.Dispose();
                throw new CaptureException($"Couldn't connect to the D-Bus session bus ({ex.Message}).", ex);
            }
            var portal = new Portal(connection, log);
            log.Info($"Portal: connected to the session bus as {connection.UniqueName}.");
            await portal.RegisterHostAppAsync().ConfigureAwait(false);
            _shared = portal;
            return portal;
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <summary>
    /// Tells the portal this unsandboxed program is "squaresnap" (xdg-desktop-portal 1.19 and later), so KDE Plasma
    /// can match it to squaresnap.desktop for its permission and shortcut dialogs. Must come before any other portal
    /// call on the connection. Older portals don't have it; that's logged and harmless.
    /// </summary>
    private async Task RegisterHostAppAsync()
    {
        try
        {
            var call = Build(ObjectPath, "org.freedesktop.host.portal.Registry", "Register", "sa{sv}", static (ref MessageWriter w, string _) =>
            {
                w.WriteString(DisplayChoice.AppId);
                var d = w.WriteDictionaryStart();
                w.WriteDictionaryEnd(d);
            }, "");
            await Connection.CallMethodAsync(call).ConfigureAwait(false);
            (HostApp, string text) = LinuxPlan.HostAppRegistration(null, null);
            _log.Info($"Portal: {text}.");
        }
        catch (DBusErrorReplyException ex)
        {
            (HostApp, string text) = LinuxPlan.HostAppRegistration(ex.ErrorName, ex.ErrorMessage);
            _log.Info($"Portal: {text}.");
        }
    }

    /// <summary>What the host registry said about "squaresnap" (the hotkey waits for install.sh when it's NotInstalled).</summary>
    public HostAppStatus HostApp { get; private set; } = HostAppStatus.Other;

    /// <summary>The Sender part of a request path: the unique name without ':' and with '.' as '_'.</summary>
    public string SenderToken => (Connection.UniqueName ?? "").TrimStart(':').Replace('.', '_');

    public static string NewToken() => "squaresnap_" + Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();

    /// <summary>
    /// Calls a portal request method and waits for its Response signal. <paramref name="timeout"/> covers the time
    /// the desktop may spend showing a dialog. Throws <see cref="DBusErrorReplyException"/> when the method itself fails
    /// (for example, the portal doesn't offer that interface) and <see cref="TimeoutException"/> when no answer comes.
    /// </summary>
    public async Task<PortalResponse> RequestAsync(string @interface, string member, string signature, PortalBodyWriter body,
        TimeSpan timeout, CancellationToken ct = default)
    {
        string token = NewToken();
        string expected = $"/org/freedesktop/portal/desktop/request/{SenderToken}/{token}";
        var answer = new TaskCompletionSource<PortalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);

        async Task<IDisposable> Subscribe(string path) => await Connection.AddMatchAsync(
                new MatchRule { Type = MessageType.Signal, Interface = "org.freedesktop.portal.Request", Member = "Response", Path = path },
                static (Message m, object? _) =>
                {
                    var r = m.GetBodyReader();
                    uint code = r.ReadUInt32();
                    return new PortalResponse(code, r.ReadDictionaryOfStringToVariantValue());
                },
                (Notification<PortalResponse> n) =>
                {
                    if (n.HasValue) answer.TrySetResult(n.Value);
                    else if (n.IsCompletion && n.Exception is { } ex) answer.TrySetException(ex);
                },
                false, ObserverFlags.None, null).ConfigureAwait(false);

        var subscriptions = new List<IDisposable> { await Subscribe(expected).ConfigureAwait(false) };
        try
        {
            string handle = await Connection.CallMethodAsync(Build(ObjectPath, @interface, member, signature, body, token),
                static (Message m, object? _) => m.GetBodyReader().ReadObjectPathAsString(), null).ConfigureAwait(false);
            if (handle != expected)
            {
                // Portals older than 0.9 don't use the token in the path; listen on the path they gave instead.
                _log.Debug($"Portal: the request path is {handle}, not {expected}; listening there.");
                subscriptions.Add(await Subscribe(handle).ConfigureAwait(false));
            }
            _log.Debug($"Portal: {@interface}.{member} sent (request {handle}); waiting up to {timeout.TotalSeconds:0} s for the answer.");

            var done = await Task.WhenAny(answer.Task, Task.Delay(timeout, ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            if (done != answer.Task)
                throw new TimeoutException($"The desktop portal didn't answer {member} within {timeout.TotalSeconds:0} seconds.");
            return await answer.Task.ConfigureAwait(false);
        }
        finally
        {
            foreach (var s in subscriptions) s.Dispose();
        }
    }

    /// <summary>A method call to the portal service, built outside any async method (MessageWriter is a ref struct).</summary>
    public MessageBuffer Build(string path, string @interface, string member, string signature, PortalBodyWriter body, string token,
        string destination = Service)
    {
        var w = Connection.GetMessageWriter();
        try
        {
            w.WriteMethodCallHeader(destination: destination, path: path, @interface: @interface, member: member, signature: signature,
                flags: MessageFlags.None);
            body(ref w, token);
            return w.CreateMessage();
        }
        finally
        {
            w.Dispose();
        }
    }

    /// <summary>Writes an a{sv} with the handle token plus the given string and boolean options.</summary>
    public static void WriteOptions(ref MessageWriter w, string token, IReadOnlyList<(string Key, object Value)>? extra = null)
    {
        var d = w.WriteDictionaryStart();
        w.WriteDictionaryEntryStart();
        w.WriteString("handle_token");
        w.WriteVariantString(token);
        foreach (var (key, value) in extra ?? [])
        {
            w.WriteDictionaryEntryStart();
            w.WriteString(key);
            switch (value)
            {
                case bool b: w.WriteVariantBool(b); break;
                case uint u: w.WriteVariantUInt32(u); break;
                default: w.WriteVariantString(value.ToString() ?? ""); break;
            }
        }
        w.WriteDictionaryEnd(d);
    }

    /// <summary>
    /// True when a tray host (StatusNotifierWatcher) runs: always on KDE Plasma; on GNOME only with the AppIndicator
    /// extension. Avalonia's Linux tray icon needs it, so without one "close to tray" would hide the window for good.
    /// </summary>
    public static async Task<bool> TrayHostRunningAsync(Logger log)
    {
        try
        {
            var portal = await GetAsync(log).ConfigureAwait(false);
            var call = portal.Build("/org/freedesktop/DBus", "org.freedesktop.DBus", "NameHasOwner", "s",
                static (ref MessageWriter w, string _) => w.WriteString("org.kde.StatusNotifierWatcher"), "", destination: "org.freedesktop.DBus");
            bool has = await portal.Connection.CallMethodAsync(call, static (Message m, object? _) => m.GetBodyReader().ReadBool(), null).ConfigureAwait(false);
            log.Info(has ? "Tray: a tray host (StatusNotifierWatcher) is running." :
                "Tray: no tray host (StatusNotifierWatcher) is running, so there's no tray icon; on GNOME, the AppIndicator extension adds one. Closing the window exits.");
            return has;
        }
        catch (Exception ex) when (ex is CaptureException or DBusExceptionBase)
        {
            log.Info($"Tray: couldn't check for a tray host ({ex.Message}); treating it as missing.");
            return false;
        }
    }

    /// <summary>A string result, whether the portal sent it as a string or an object path.</summary>
    public static string? StringResult(Dictionary<string, VariantValue> results, string key)
    {
        if (!results.TryGetValue(key, out var v)) return null;
        if (v.Type == VariantValueType.Variant) v = v.GetVariantValue();
        return v.Type switch
        {
            VariantValueType.String => v.GetString(),
            VariantValueType.ObjectPath => v.GetObjectPathAsString(),
            _ => v.ToString(),
        };
    }

    public void Dispose() => Connection.Dispose();
}
