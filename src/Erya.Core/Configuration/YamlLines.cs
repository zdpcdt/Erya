namespace Erya.Core.Configuration;

/// <summary>
/// Line-level helpers shared by the YAML patch writers. RIME user files must
/// be edited surgically (YamlDotNet drops comments on round-trips), so both
/// <see cref="DefaultCustomYamlEditor"/> and <see cref="CustomYamlPatcher"/>
/// operate on raw text lines through these primitives.
/// </summary>
internal static class YamlLines
{
    /// <summary>True when the line is a mapping key line for <paramref name="key"/> (quoted or plain).</summary>
    public static bool IsKeyLine(string line, string key) => KeyTextOf(line) == key;

    /// <summary>
    /// Extracts the mapping key text of a line, or null when the line is not
    /// a key line. Handles simple single/double quoted keys; the closing
    /// colon (and anything after it) is not part of the key.
    /// </summary>
    public static string? KeyTextOf(string line)
    {
        var trimmed = line.Trim();
        if (trimmed.Length == 0) return null;

        int keyEnd;
        if (trimmed.StartsWith('"') || trimmed.StartsWith('\''))
        {
            char quote = trimmed[0];
            int close = trimmed.IndexOf(quote, 1);
            if (close <= 1) return null;
            var key = trimmed[1..close];
            if (!trimmed[(close + 1)..].StartsWith(':')) return null;
            return Unescape(key);
        }

        keyEnd = trimmed.IndexOf(':');
        if (keyEnd <= 0) return null;
        var rest = trimmed[(keyEnd + 1)..];
        if (rest.Length > 0 && !char.IsWhiteSpace(rest[0]) && !rest.StartsWith('#')) return null;
        return trimmed[..keyEnd].TrimEnd();
    }

    /// <summary>True when the key line carries a value on the same line.</summary>
    public static bool HasInlineValue(string line)
    {
        int colon = line.IndexOf(':');
        return colon >= 0 && line[(colon + 1)..].Trim() is { Length: > 0 } rest && !rest.StartsWith('#');
    }

    public static bool IsBlankOrComment(string line)
        => line.Trim().Length == 0 || line.Trim().StartsWith('#');

    public static int IndentOf(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i;
    }

    /// <summary>Splits a file into normalized lines (no BOM/EOL noise) plus its dominant EOL.</summary>
    public static (List<string> Lines, string Eol) SplitLines(string text)
    {
        var eol = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = text.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();
        return (lines, eol);
    }

    public static string Join(List<string> lines, string eol) => string.Join(eol, lines) + eol;

    private static string Unescape(string key)
        => key.Replace("\\\"", "\"").Replace("\\\\", "\\").Replace("''", "'");
}
