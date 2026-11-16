using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace SquareSnap.Core.Naming;

/// <summary>
/// Builds file names from a template. Tokens:
/// <list type="bullet">
/// <item><c>{date}</c>: yyyyMMdd</item>
/// <item><c>{time}</c>: HHmmss</item>
/// <item><c>{size}</c>: side length in pixels of the saved image</item>
/// <item><c>{n}</c> or <c>{n:000}</c>: a counter that starts at 1 each day (zero-padded to as many digits as there are zeros)</item>
/// </list>
/// The counter is worked out from the files already in the folder, so it resets daily, never repeats a name,
/// and needs no stored state.
/// </summary>
public static class FileNamer
{
    public const string DefaultTemplate = "SquareCap-{date}-{n:000}";

    private static readonly Regex TokenRegex = new(@"\{([a-z]+)(?::(0+))?\}", RegexOptions.CultureInvariant);
    private static readonly char[] BadChars = ['<', '>', ':', '"', '/', '\\', '|', '?', '*'];
    private static readonly HashSet<string> KnownTokens = ["date", "time", "size", "n"];

    /// <summary>Checks a template; returns null when it's fine, or a message saying what to fix.</summary>
    public static string? Validate(string? template)
    {
        if (string.IsNullOrWhiteSpace(template))
            return "The file name template is empty. Example: " + DefaultTemplate;

        foreach (Match m in TokenRegex.Matches(template))
        {
            if (!KnownTokens.Contains(m.Groups[1].Value))
                return $"Unknown token {m.Value} in the file name template. Use {{date}}, {{time}}, {{size}} or {{n:000}}.";
            if (m.Groups[2].Success && m.Groups[1].Value != "n")
                return $"Only {{n}} takes a padding (as in {{n:000}}); {m.Value} does not.";
        }

        string literal = TokenRegex.Replace(template, "");
        if (literal.Contains('{') || literal.Contains('}'))
            return "The file name template has a stray { or }. Tokens look like {date} or {n:000}.";
        int bad = literal.IndexOfAny(BadChars);
        if (bad >= 0)
            return $"The file name template contains '{literal[bad]}', which can't be used in file names.";
        if (literal.Any(char.IsControl))
            return "The file name template contains a control character.";
        if (template.EndsWith('.') || template.EndsWith(' '))
            return "The file name template can't end with a dot or a space (Windows refuses such names).";
        return null;
    }

    /// <summary>
    /// The next free file name (with extension) for <paramref name="template"/> in a folder that already holds
    /// <paramref name="existingNames"/> (bare file names, not paths).
    /// </summary>
    public static string NextName(string template, DateTime now, int side, ImageFormat format, IEnumerable<string> existingNames)
    {
        string? problem = Validate(template);
        if (problem != null) throw new ArgumentException(problem, nameof(template));

        var existing = new HashSet<string>(existingNames, StringComparer.OrdinalIgnoreCase);
        string ext = format.Extension();
        bool hasCounter = TokenRegex.Matches(template).Any(m => m.Groups[1].Value == "n");

        if (hasCounter)
        {
            int next = HighestCounter(template, now, side, existing) + 1;
            while (true)
            {
                string name = Render(template, now, side, next) + ext;
                if (!existing.Contains(name)) return name;
                next++;
            }
        }

        // No counter in the template: add -2, -3 ... only when the plain name is already taken.
        string baseName = Render(template, now, side, 0);
        if (!existing.Contains(baseName + ext)) return baseName + ext;
        for (int i = 2; ; i++)
        {
            string name = $"{baseName}-{i}{ext}";
            if (!existing.Contains(name)) return name;
        }
    }

    /// <summary>The template filled in with a given counter value (no extension).</summary>
    public static string Render(string template, DateTime now, int side, int counter) =>
        TokenRegex.Replace(template, m => m.Groups[1].Value switch
        {
            "date" => now.ToString("yyyyMMdd", CultureInfo.InvariantCulture),
            "time" => now.ToString("HHmmss", CultureInfo.InvariantCulture),
            "size" => side.ToString(CultureInfo.InvariantCulture),
            "n" => counter.ToString(CultureInfo.InvariantCulture).PadLeft(m.Groups[2].Success ? m.Groups[2].Length : 1, '0'),
            _ => m.Value,
        });

    /// <summary>The highest counter used today by files that match the template (any SquareSnap extension), or 0.</summary>
    internal static int HighestCounter(string template, DateTime now, int side, IEnumerable<string> existingNames)
    {
        var pattern = new StringBuilder("^");
        int last = 0;
        foreach (Match m in TokenRegex.Matches(template))
        {
            pattern.Append(Regex.Escape(template[last..m.Index]));
            pattern.Append(m.Groups[1].Value switch
            {
                "date" => Regex.Escape(now.ToString("yyyyMMdd", CultureInfo.InvariantCulture)),
                "time" => @"\d{6}",
                "size" => @"\d+",
                _ => @"(?<n>\d+)",
            });
            last = m.Index + m.Length;
        }
        pattern.Append(Regex.Escape(template[last..]));
        pattern.Append(@"(?:-\d+)?\.(?:png|jpe?g|webp)$");

        var rx = new Regex(pattern.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        int max = 0;
        foreach (string name in existingNames)
        {
            var m = rx.Match(name);
            if (m.Success && int.TryParse(m.Groups["n"].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int n) && n > max)
                max = n;
        }
        return max;
    }
}
