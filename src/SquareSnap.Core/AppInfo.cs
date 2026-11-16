using System.Reflection;

namespace SquareSnap.Core;

public static class AppInfo
{
    public const string Name = "SquareSnap";

    /// <summary>The version from Directory.Build.props, such as 2.0.0-beta.1 (without the +commit suffix the SDK adds).</summary>
    public static string Version { get; } = ReadVersion();

    private static string ReadVersion()
    {
        string? v = typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (string.IsNullOrEmpty(v)) return "0.0.0";
        int plus = v.IndexOf('+');
        return plus >= 0 ? v[..plus] : v;
    }
}
