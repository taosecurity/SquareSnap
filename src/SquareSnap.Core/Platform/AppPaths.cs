namespace SquareSnap.Core.Platform;

/// <summary>
/// Where SquareSnap keeps its files. Every path can be worked out for either OS from an environment lookup,
/// so tests check the Linux rules on Windows and the other way round.
/// </summary>
public sealed class AppPaths
{
    public const string SettingsFileName = "settings.json";
    public const string LogFileName = "squaresnap.log";

    /// <summary>%APPDATA%\SquareSnap on Windows (the same folder 1.0 used), $XDG_CONFIG_HOME/SquareSnap or ~/.config/SquareSnap on Linux.</summary>
    public string ConfigDir { get; }

    /// <summary>Pictures\SquareSnap (Windows) or ~/Pictures/SquareSnap (Linux).</summary>
    public string DefaultSaveFolder { get; }

    /// <summary>BejSnap 1.0's settings (QSettings INI), read once to import the save folder and theme. Linux only.</summary>
    public string? BejSnapSettingsFile { get; }

    /// <summary>Directory separator for the OS these paths belong to (not necessarily the OS running the code).</summary>
    public char Separator { get; }

    public string SettingsFile => ConfigDir + Separator + SettingsFileName;
    public string DefaultLogFile => ConfigDir + Separator + LogFileName;

    private AppPaths(string configDir, string defaultSaveFolder, string? bejSnapSettingsFile, char separator)
    {
        Separator = separator;
        ConfigDir = configDir;
        DefaultSaveFolder = defaultSaveFolder;
        BejSnapSettingsFile = bejSnapSettingsFile;
    }

    public static AppPaths ForCurrentOs() => OperatingSystem.IsWindows()
        ? ForWindows(Environment.GetEnvironmentVariable, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures))
        : ForLinux(Environment.GetEnvironmentVariable);

    /// <summary><paramref name="picturesFolder"/> is the shell's Pictures folder (it can be moved, so it isn't derived from %USERPROFILE%).</summary>
    public static AppPaths ForWindows(Func<string, string?> env, string? picturesFolder)
    {
        // Joined by hand with '\' so the Windows rules give the same answer when tests run on Linux.
        string appData = (NonEmpty(env("APPDATA"))
            ?? (NonEmpty(env("USERPROFILE")) ?? @"C:\Users\Default") + @"\AppData\Roaming").TrimEnd('\\');
        string pictures = (NonEmpty(picturesFolder) ?? (NonEmpty(env("USERPROFILE")) ?? appData) + @"\Pictures").TrimEnd('\\');
        return new AppPaths(appData + @"\SquareSnap", pictures + @"\SquareSnap", null, '\\');
    }

    public static AppPaths ForLinux(Func<string, string?> env)
    {
        string home = (NonEmpty(env("HOME")) ?? "/tmp").TrimEnd('/');
        string config = (NonEmpty(env("XDG_CONFIG_HOME")) ?? home + "/.config").TrimEnd('/');
        string pictures = (NonEmpty(env("XDG_PICTURES_DIR")) ?? home + "/Pictures").TrimEnd('/');
        return new AppPaths(config + "/SquareSnap", pictures + "/SquareSnap", config + "/BejSnap/BejSnap.conf", '/');
    }

    /// <summary>
    /// Where a file SquareSnap wrote really is, for showing and opening it from outside SquareSnap (Explorer, Notepad)
    /// (step 6, 2026-11-14, BejView's code). The Microsoft Store package keeps Windows' default file-system write
    /// virtualization (the user's decision, 2026-11-13), so NEW files under %APPDATA% go to a private copy,
    /// %LOCALAPPDATA%\Packages\&lt;family name&gt;\LocalCache\Roaming\...; files that already exist there (1.0's
    /// settings, a zip build's log) are changed in place. SquareSnap itself sees the merged view, but other programs
    /// only see the private copy. So when the private copy exists, this returns it; otherwise (zip, WinGet, Linux, or
    /// a file that was already there) the path is returned unchanged.
    /// </summary>
    public static string RealLocation(string path) => RealLocation(path, PackageIdentity.FamilyName,
        Environment.GetEnvironmentVariable, p => File.Exists(p) || Directory.Exists(p));

    public static string RealLocation(string path, string? packageFamilyName, Func<string, string?> env, Func<string, bool> exists)
    {
        if (packageFamilyName == null) return path;
        string? appData = NonEmpty(env("APPDATA"))?.TrimEnd('\\');
        string? localAppData = NonEmpty(env("LOCALAPPDATA"))?.TrimEnd('\\');
        if (appData == null || localAppData == null) return path;
        if (!path.StartsWith(appData + @"\", StringComparison.OrdinalIgnoreCase)) return path;
        string copy = $@"{localAppData}\Packages\{packageFamilyName}\LocalCache\Roaming{path[appData.Length..]}";
        return exists(copy) ? copy : path;
    }

    private static string? NonEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
