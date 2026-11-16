namespace SquareSnap.Core.Naming;

public enum ImageFormat
{
    Png,
    Jpeg,
    Webp,
}

public static class ImageFormats
{
    public static string Extension(this ImageFormat f) => f switch
    {
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Webp => ".webp",
        _ => ".png",
    };

    /// <summary>Every extension SquareSnap writes, used when counting today's files.</summary>
    public static IReadOnlyList<string> AllExtensions { get; } = [".png", ".jpg", ".jpeg", ".webp"];

    public static bool TryParse(string? text, out ImageFormat format)
    {
        format = ImageFormat.Png;
        switch (text?.Trim().TrimStart('.').ToLowerInvariant())
        {
            case "png": format = ImageFormat.Png; return true;
            case "jpg" or "jpeg": format = ImageFormat.Jpeg; return true;
            case "webp": format = ImageFormat.Webp; return true;
            default: return false;
        }
    }
}
