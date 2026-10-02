using System.Text;
using YamlDotNet.RepresentationModel;

namespace Erya.Core.Configuration;

/// <summary>
/// Writes the <c>patch/schema_list</c> section of <c>default.custom.yaml</c>
/// <b>without rewriting the rest of the file</b>.
/// <para>
/// YamlDotNet cannot round-trip comments (verified: its parser produces no
/// comment events and its emitter drops any), so a naive object round-trip
/// would destroy user data. Instead we do line-level surgery: replace/insert
/// only the schema_list block, byte-preserving everything else — then
/// re-parse the result and roll back from the automatic <c>.bak</c> backup
/// if verification fails.
/// </para>
/// </summary>
internal static class DefaultCustomYamlEditor
{
    public const string FileName = "default.custom.yaml";
    private const string BackupSuffix = ".bak";

    public static void WriteSchemaListPatch(string userDir, IReadOnlyList<string> schemaIds)
    {
        Directory.CreateDirectory(userDir);
        var path = Path.Combine(userDir, FileName);

        // UI never yields duplicates, but guard anyway so the file we write
        // round-trips through our own reader exactly.
        List<string> ids = [.. schemaIds.Distinct(StringComparer.Ordinal)];

        if (!File.Exists(path))
        {
            File.WriteAllText(path, RenderFresh(ids));
            VerifyOrThrow(path, ids);
            return;
        }

        var original = File.ReadAllText(path);
        var backupPath = path + BackupSuffix;
        File.WriteAllText(backupPath, original);

        var eol = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var lines = original.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').ToList();

        string edited = TryEditLines(lines, ids, eol) ?? RewritePreservingStructure(original, ids);
        File.WriteAllText(path, edited);
        VerifyOrThrow(path, ids);
    }

    /// <summary>Restores the backup when the written file does not re-parse to the expected ids.</summary>
    private static void VerifyOrThrow(string path, IReadOnlyList<string> expected)
    {
        if (Verify(path, expected)) return;
        var backupPath = path + BackupSuffix;
        if (File.Exists(backupPath)) File.WriteAllText(path, File.ReadAllText(backupPath));
        throw new InvalidOperationException(
            $"写入 {FileName} 后未能通过校验，原文件已还原。");
    }

    private static bool Verify(string path, IReadOnlyList<string> expected)
    {
        var root = RimeYaml.LoadRoot(path);
        if (root is null) return false;
        if (!TryReadPatchedList(root, out var actual)) return false;
        return actual.SequenceEqual(expected, StringComparer.Ordinal);
    }

    /// <summary>
    /// Line-level edit: replace the schema_list value under an existing
    /// <c>patch:</c> block (nested or flat key form), insert it right after
    /// <c>patch:</c>, or append a whole new <c>patch:</c> block at EOF.
    /// Returns null when the shape is not one we can edit surgically
    /// (caller falls back to a structural rewrite).
    /// </summary>
    private static string? TryEditLines(List<string> lines, IReadOnlyList<string> ids, string eol)
    {
        int patchIdx = lines.FindIndex(l => IndentOf(l) == 0 && IsKeyLine(l, "patch"));
        bool patchHasInlineValue = patchIdx >= 0 && HasInlineValue(lines[patchIdx]);

        // 1) nested "patch:" mapping with (or without) a schema_list child
        if (patchIdx >= 0 && !patchHasInlineValue)
        {
            int childIdx = FindChildKeyInBlock(lines, patchIdx, "schema_list");
            if (childIdx >= 0)
            {
                ReplaceValueExtent(lines, childIdx, ids);
                return Join(lines, eol);
            }
            lines.InsertRange(patchIdx + 1, KeyAndItems(2, ids));
            return Join(lines, eol);
        }

        // 2) flat "patch/schema_list:" / "patch/schema_list/+:" top-level key
        foreach (var key in (string[])["patch/schema_list", "patch/schema_list/+"])
        {
            int keyIdx = lines.FindIndex(l => IndentOf(l) == 0 && IsKeyLine(l, key));
            if (keyIdx >= 0)
            {
                ReplaceValueExtent(lines, keyIdx, ids);
                return Join(lines, eol);
            }
        }

        // 3) no usable patch section at all → append one
        if (patchIdx < 0)
        {
            if (lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add("patch:");
            lines.AddRange(KeyAndItems(2, ids));
            return Join(lines, eol);
        }

        return null; // "patch:" exists but in an unmanageable shape (inline flow value)
    }

    /// <summary>Structural rewrite for exotic shapes; loses comments (a .bak backup exists).</summary>
    private static string RewritePreservingStructure(string original, IReadOnlyList<string> ids)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(original));
        if (stream.Documents is not [var document, ..] || document.RootNode is not YamlMappingNode root)
            throw new InvalidOperationException(
                $"{FileName} 不是可识别的 YAML 映射，无法安全写入。请手工检查该文件。");

        if (!RimeYaml.TryGetChild(root, "patch", out var patchNode) || patchNode is not YamlMappingNode patch)
        {
            patch = new YamlMappingNode();
            root.Children.Add(new YamlScalarNode("patch"), patch);
        }
        foreach (var key in (string[])["schema_list/+", "schema_list"])
        {
            var node = new YamlScalarNode(key);
            if (patch.Children.ContainsKey(node)) patch.Children.Remove(node);
        }
        patch.Children.Add(new YamlScalarNode("schema_list"), SchemaList(ids));

        using var writer = new StringWriter();
        stream.Save(writer);
        return writer.ToString();
    }

    private static YamlSequenceNode SchemaList(IReadOnlyList<string> ids)
    {
        var list = new YamlSequenceNode();
        foreach (var id in ids)
            list.Add(new YamlMappingNode(
                new KeyValuePair<YamlNode, YamlNode>(new YamlScalarNode("schema"), new YamlScalarNode(id))));
        return list;
    }

    /// <summary>Scans the value block of <paramref name="keyIdx"/> and swaps it for the new items.</summary>
    private static void ReplaceValueExtent(List<string> lines, int keyIdx, IReadOnlyList<string> ids)
    {
        var keyLine = lines[keyIdx];
        int keyIndent = IndentOf(keyLine);

        // Block value: extend to the last line still inside this value —
        // deeper-indented lines and same-indent "- " sequence items belong
        // to the key; the first other meaningful line ends the extent.
        int end = keyIdx + 1;
        int n = lines.Count;
        while (end < n)
        {
            var line = lines[end];
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#'))
            {
                end++;
                continue;
            }
            int indent = IndentOf(line);
            if (indent <= keyIndent && !(indent == keyIndent && trimmed.StartsWith("- "))) break;
            end++;
        }
        // Keep trailing blank/comment lines (they may document the next key).
        while (end > keyIdx + 1 && IsBlankOrComment(lines[end - 1])) end--;

        lines.RemoveRange(keyIdx + 1, end - (keyIdx + 1));
        lines[keyIdx] = keyLine[..(keyLine.IndexOf(':') + 1)] + (ids.Count == 0 ? " []" : "");
        lines.InsertRange(keyIdx + 1, Items(keyIndent, ids));
    }

    /// <summary>Finds a child key inside the block started at <paramref name="parentIdx"/>.</summary>
    private static int FindChildKeyInBlock(List<string> lines, int parentIdx, string key)
    {
        int parentIndent = IndentOf(lines[parentIdx]);
        for (int i = parentIdx + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            var trimmed = line.Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) continue;
            int indent = IndentOf(line);
            if (indent <= parentIndent) return -1; // parent block ended
            if (IsKeyLine(line, key)) return i;
        }
        return -1;
    }

    private static List<string> KeyAndItems(int indent, IReadOnlyList<string> ids)
    {
        List<string> lines = [ids.Count == 0
            ? $"{new string(' ', indent)}schema_list: []"
            : $"{new string(' ', indent)}schema_list:"];
        lines.AddRange(Items(indent, ids));
        return lines;
    }

    private static List<string> Items(int keyIndent, IReadOnlyList<string> ids)
        => ids.Select(id => $"{new string(' ', keyIndent + 2)}- schema: {QuoteIfNeeded(id)}").ToList();

    private static string RenderFresh(IReadOnlyList<string> ids)
    {
        var sb = new StringBuilder();
        sb.AppendLine("# default.custom.yaml —— RIME 用户补丁配置");
        sb.AppendLine("# 部署时会覆盖 default.yaml 中的同名设置；修改后需重新部署生效。");
        sb.AppendLine("# 尔雅 Erya 只维护 patch/schema_list（输入方案列表），其余内容可手工编辑。");
        sb.AppendLine();
        sb.AppendLine("patch:");
        foreach (var line in KeyAndItems(2, ids)) sb.AppendLine(line);
        return sb.ToString();
    }

    private static bool TryReadPatchedList(YamlMappingNode root, out IReadOnlyList<string> ids)
    {
        if (RimeYaml.TryGetChild(root, "patch", out var patchNode) && patchNode is YamlMappingNode patch
            && RimeYaml.TryGetChild(patch, "schema_list", out var list))
        {
            ids = RimeYaml.ReadSchemaList(list);
            return true;
        }
        if (RimeYaml.TryGetChild(root, "patch/schema_list", out var flat))
        {
            ids = RimeYaml.ReadSchemaList(flat);
            return true;
        }
        ids = [];
        return false;
    }

    private static bool IsKeyLine(string line, string key)
    {
        var trimmed = line.Trim();
        if (!trimmed.StartsWith(key, StringComparison.Ordinal)) return false;
        var rest = trimmed[key.Length..];
        return rest.StartsWith(':') && (rest.Length == 1 || char.IsWhiteSpace(rest[1]));
    }

    private static bool HasInlineValue(string line)
    {
        int colon = line.IndexOf(':');
        return colon >= 0 && line[(colon + 1)..].Trim() is { Length: > 0 } rest && !rest.StartsWith('#');
    }

    private static bool IsBlankOrComment(string line)
        => line.Trim().Length == 0 || line.Trim().StartsWith('#');

    private static int IndentOf(string line)
    {
        int i = 0;
        while (i < line.Length && (line[i] == ' ' || line[i] == '\t')) i++;
        return i;
    }

    private static string Join(List<string> lines, string eol) => string.Join(eol, lines) + eol;

    private static string QuoteIfNeeded(string id)
        => System.Text.RegularExpressions.Regex.IsMatch(id, @"^[A-Za-z0-9_.\-+]+$")
            ? id
            : "\"" + id.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
}
