using System.Text.Json;
using System.Text.Json.Serialization;

namespace SquareSnap.Core.Settings;

/// <summary>Source-generated JSON (works with trimming and single-file builds).</summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    UseStringEnumConverter = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true)]
[JsonSerializable(typeof(AppSettings))]
[JsonSerializable(typeof(LegacySettings))]
internal sealed partial class SettingsJsonContext : JsonSerializerContext;

/// <summary>The 1.0 settings file: {"DefaultSaveLocation": "...", "NextFileNumber": 7}.</summary>
internal sealed class LegacySettings
{
    public string? DefaultSaveLocation { get; set; }
    public int NextFileNumber { get; set; }
}
