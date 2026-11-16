using System.Text.Json;
using SquareSnap.Core.Logging;
using SquareSnap.Core.Platform;

namespace SquareSnap.Core.Settings;

/// <summary>
/// Loads and saves <see cref="AppSettings"/>. Never throws on a bad file: it logs what was wrong, keeps a copy of the
/// bad file next to it, and carries on with defaults.
/// <para>First run imports older settings once: SquareSnap 1.0's settings.json (same Windows folder, no SchemaVersion)
/// and BejSnap 1.0's ~/.config/BejSnap/BejSnap.conf on Linux.</para>
/// </summary>
public sealed class SettingsStore(AppPaths paths, Logger log)
{
    public AppPaths Paths { get; } = paths;

    public AppSettings Load()
    {
        string file = Paths.SettingsFile;
        AppSettings settings;

        if (File.Exists(file))
        {
            settings = ReadFile(file) ?? new AppSettings();
        }
        else
        {
            log.Info($"No settings file at {file}; starting with defaults.");
            settings = new AppSettings();
            if (ImportBejSnap(settings)) Save(settings);
        }

        foreach (string fix in settings.Normalize())
            log.Warning($"Settings ({file}): {fix}");
        return settings;
    }

    /// <summary>Writes the settings atomically (temporary file, then rename), so a crash can't leave half a file.</summary>
    public bool Save(AppSettings settings)
    {
        string file = Paths.SettingsFile;
        try
        {
            Directory.CreateDirectory(Paths.ConfigDir);
            string tmp = file + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(settings, SettingsJsonContext.Default.AppSettings));
            File.Move(tmp, file, overwrite: true);
            log.Debug($"Settings saved to {file}.");
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"Can't save settings to {file}. Check that the folder exists and isn't read-only", ex);
            return false;
        }
    }

    /// <summary>The LastRegion in the settings file as it is now (another SquareSnap may have saved one since this
    /// run loaded its settings), or null when the file is missing or unreadable. Quiet: logs nothing.</summary>
    public string? PeekLastRegion()
    {
        try
        {
            string file = Paths.SettingsFile;
            if (!File.Exists(file)) return null;
            return JsonSerializer.Deserialize(File.ReadAllText(file), SettingsJsonContext.Default.AppSettings)?.LastRegion;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>The folder captures go to: the saved choice, or Pictures/SquareSnap.</summary>
    public string SaveFolderFor(AppSettings s) =>
        string.IsNullOrWhiteSpace(s.SaveFolder) ? Paths.DefaultSaveFolder : s.SaveFolder;

    private AppSettings? ReadFile(string file)
    {
        string json;
        try
        {
            json = File.ReadAllText(file);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Error($"Can't read settings file {file}; using defaults this time", ex);
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                throw new JsonException("The top level is not a JSON object.");

            bool isV2 = doc.RootElement.EnumerateObject()
                .Any(p => p.Name.Equals(nameof(AppSettings.SchemaVersion), StringComparison.OrdinalIgnoreCase));
            if (isV2)
            {
                var s = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.AppSettings) ?? new AppSettings();
                log.Debug($"Settings read from {file}.");
                return s;
            }

            return ImportV1(file, json);
        }
        catch (JsonException ex)
        {
            string bad = file + ".bad";
            try { File.Copy(file, bad, overwrite: true); }
            catch (Exception copyEx) when (copyEx is IOException or UnauthorizedAccessException) { /* keep going with defaults */ }
            log.Warning($"Settings file {file} is not valid JSON ({ex.Message}). Using defaults; the old file was copied to {bad}. " +
                        "Fix or delete it to stop this warning.");
            return null;
        }
    }

    /// <summary>1.0 kept DefaultSaveLocation and NextFileNumber. The folder carries over; the counter isn't needed any more.</summary>
    private AppSettings ImportV1(string file, string json)
    {
        var old = JsonSerializer.Deserialize(json, SettingsJsonContext.Default.LegacySettings) ?? new LegacySettings();
        var s = new AppSettings { SaveFolder = string.IsNullOrWhiteSpace(old.DefaultSaveLocation) ? null : old.DefaultSaveLocation };

        string backup = Path.Combine(Path.GetDirectoryName(file) ?? ".", "settings.v1.json");
        try { File.Copy(file, backup, overwrite: false); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { /* a backup exists, or the folder is read-only */ }
        log.Info($"Imported SquareSnap 1.0 settings from {file} (save folder: {s.SaveFolder ?? "default"}); the 1.0 file is kept as {backup}.");
        Save(s);
        return s;
    }

    /// <summary>BejSnap stored savePath and darkMode in a QSettings INI file under [General].</summary>
    private bool ImportBejSnap(AppSettings s)
    {
        string? conf = Paths.BejSnapSettingsFile;
        if (conf == null || !File.Exists(conf)) return false;
        try
        {
            var values = ReadQSettingsGeneral(File.ReadAllLines(conf));
            bool any = false;
            if (values.TryGetValue("savePath", out string? path) && path.Length > 0) { s.SaveFolder = path; any = true; }
            if (values.TryGetValue("darkMode", out string? dark) && bool.TryParse(dark, out bool d)) { s.DarkMode = d; any = true; }
            if (any) log.Info($"Imported BejSnap settings from {conf} (save folder: {s.SaveFolder ?? "default"}, dark mode: {s.DarkMode}).");
            return any;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            log.Warning($"Found BejSnap settings at {conf} but couldn't read them ({ex.Message}); skipping the import.");
            return false;
        }
    }

    /// <summary>Reads key=value pairs from the [General] section of a QSettings INI file, undoing QSettings' quoting.</summary>
    internal static Dictionary<string, string> ReadQSettingsGeneral(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        bool inGeneral = false;
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith(';') || line.StartsWith('#')) continue;
            if (line.StartsWith('['))
            {
                inGeneral = line.Equals("[General]", StringComparison.OrdinalIgnoreCase);
                continue;
            }
            if (!inGeneral) continue;
            int eq = line.IndexOf('=');
            if (eq <= 0) continue;
            string value = line[(eq + 1)..].Trim();
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"') value = value[1..^1];
            value = value.Replace("\\\\", "\u0000").Replace("\\\"", "\"").Replace("\u0000", "\\");
            result[line[..eq].Trim()] = value;
        }
        return result;
    }
}
