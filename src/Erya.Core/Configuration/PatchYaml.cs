using System.Globalization;
using YamlDotNet.RepresentationModel;

namespace Erya.Core.Configuration;

/// <summary>
/// Converts between typed CLR values (what the settings UI edits) and YAML
/// text (what gets spliced into <c>.custom.yaml</c>). We own the emitter on
/// purpose: values are small and fully typed here, and this keeps block
/// indentation consistent with the line-level patch writer.
/// </summary>
public static class PatchYaml
{
    /// <summary>
    /// Renders one patch entry against a key line at
    /// <paramref name="keyIndent"/>: scalars and empty collections come back
    /// inline (to append after <c>key:</c>); block collections come back as
    /// fully indented lines (to insert below the key line).
    /// </summary>
    public static (string? Inline, List<string>? Block) RenderEntry(object? value, int keyIndent)
    {
        switch (value)
        {
            case IReadOnlyList<object?> { Count: 0 }:
                return ("[]", null);
            case IReadOnlyDictionary<string, object?> { Count: 0 }:
                return ("{}", null);
            case IReadOnlyList<object?> or IReadOnlyDictionary<string, object?>:
                return (null, RenderBlock(value, keyIndent + 2));
            default:
                return (RenderInline(value), null);
        }
    }

    /// <summary>Renders a scalar (or null) as an inline YAML fragment.</summary>
    public static string RenderInline(object? value) => value switch
    {
        null => "null",
        bool b => b ? "true" : "false",
        long l => l.ToString(CultureInfo.InvariantCulture),
        int i => i.ToString(CultureInfo.InvariantCulture),
        double d => d.ToString("0.0###############", CultureInfo.InvariantCulture),
        string s => ScalarText(s),
        _ => throw new NotSupportedException($"不支持的补丁值类型：{value.GetType().Name}"),
    };

    /// <summary>
    /// Renders a non-empty list or map as block lines, every line carrying
    /// its absolute indentation. List items that are maps render their first
    /// pair inline behind the dash (RIME style: <c>- schema: luna_pinyin</c>).
    /// </summary>
    public static List<string> RenderBlock(object value, int indent)
    {
        switch (value)
        {
            case IReadOnlyList<object?> items:
            {
                List<string> lines = [];
                foreach (var item in items)
                {
                    if (item is IReadOnlyDictionary<string, object?> map)
                    {
                        var rendered = MapLines(map, indent + 2);
                        rendered[0] = $"{Spaces(indent)}- " + rendered[0].TrimStart();
                        lines.AddRange(rendered);
                    }
                    else
                    {
                        lines.Add($"{Spaces(indent)}- {RenderInline(item)}");
                    }
                }
                return lines;
            }
            case IReadOnlyDictionary<string, object?> map:
                return MapLines(map, indent);
            default:
                throw new NotSupportedException($"值必须是标量、列表或映射：{value.GetType().Name}");
        }
    }

    private static List<string> MapLines(IReadOnlyDictionary<string, object?> map, int indent)
    {
        List<string> lines = [];
        foreach (var (key, item) in map)
        {
            var (inline, block) = RenderEntry(item, indent);
            if (inline is { } inlineValue)
                lines.Add($"{Spaces(indent)}{KeyText(key)}: {inlineValue}");
            else
            {
                lines.Add($"{Spaces(indent)}{KeyText(key)}:");
                lines.AddRange(block!);
            }
        }
        return lines;
    }

    /// <summary>
    /// Renders a mapping key, quoting it when it cannot appear plain (RIME
    /// patch keys routinely contain <c>/</c>, which is safe plain, but keys
    /// with colons or leading indicators must be quoted).
    /// </summary>
    public static string KeyText(string key) => NeedsQuoting(key) ? Quote(key) : key;

    /// <summary>Double-quotes a scalar string, escaping backslashes and quotes.</summary>
    public static string ScalarText(string s) => NeedsQuoting(s) ? Quote(s) : s;

    private static bool NeedsQuoting(string s)
    {
        if (s.Length == 0) return true;
        if (char.IsWhiteSpace(s[0]) || char.IsWhiteSpace(s[^1])) return true;
        if (s.Contains('"') || s.Contains('\\') || s.Contains('\n') || s.Contains('\t')) return true;
        if (s.Contains(": ") || s.EndsWith(':') || s.Contains(" #")) return true;
        if ("-?:,[]{}#&*!|>'%@`".Contains(s[0])) return true;

        // Plain YAML would read these back as another scalar type.
        return s is "true" or "True" or "TRUE" or "false" or "False" or "FALSE"
            or "null" or "Null" or "NULL" or "~" or "yes" or "no" or "on" or "off"
            || long.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out _)
            || (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)
                && !double.IsNaN(d) && !double.IsInfinity(d));
    }

    private static string Quote(string s)
        => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string Spaces(int count) => new(' ', count);

    /// <summary>
    /// Converts a YAML node read from a file into a CLR value the UI can
    /// compare and re-render (bool / long / double / string / nested lists
    /// and ordered maps). Quoted scalars always come back as strings — the
    /// quotes are what told the writer the value was meant to be text.
    /// </summary>
    public static object? ToClrValue(YamlNode? node)
    {
        return node switch
        {
            null => null,
            YamlScalarNode { Value: null } => null,
            YamlScalarNode
            {
                Style: YamlDotNet.Core.ScalarStyle.SingleQuoted or YamlDotNet.Core.ScalarStyle.DoubleQuoted,
                Value: { } quoted,
            } => quoted,
            YamlScalarNode scalar when scalar.Value is { } text => ParseScalar(text),
            YamlSequenceNode sequence => sequence.Select(ToClrValue).ToList(),
            YamlMappingNode mapping => mapping.Children
                .Where(kv => kv.Key is YamlScalarNode)
                .Select(kv => (((YamlScalarNode)kv.Key).Value!, ToClrValue(kv.Value)))
                .ToDictionary(pair => pair.Item1, pair => pair.Item2),
            _ => null,
        };
    }

    private static object? ParseScalar(string text) => text switch
    {
        "true" or "True" or "TRUE" => true,
        "false" or "False" or "FALSE" => false,
        "null" or "Null" or "NULL" or "~" => null,
        _ when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) => l,
        _ when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) => d,
        _ => text,
    };
}
