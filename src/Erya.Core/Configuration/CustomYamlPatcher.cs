using YamlDotNet.RepresentationModel;

namespace Erya.Core.Configuration;

/// <summary>
/// Reads and writes arbitrary keys in a RIME <c>*.custom.yaml</c> patch
/// without rewriting the rest of the file — same line-surgery policy as
/// <see cref="DefaultCustomYamlEditor"/> (byte-preserving outside the edited
/// block, automatic <c>.bak</c>, re-parse verification with rollback).
/// <para>
/// RIME accepts three patch shapes and all are understood here:
/// <list type="number">
/// <item>nested — <c>patch:</c> → <c>switcher:</c> → <c>hotkeys:</c></item>
/// <item>flat-in-patch — <c>patch:</c> → <c>switcher/hotkeys:</c></item>
/// <item>flat-top — <c>patch/switcher/hotkeys:</c></item>
/// </list>
/// Writes always use shape 2; pre-existing representations of the written
/// keys (any shape, including <c>/+</c> append forms) are migrated to it.
/// </para>
/// </summary>
internal static class CustomYamlPatcher
{
    private const string BackupSuffix = ".bak";

    /// <summary>
    /// Reads the patch dictionary in file order. Keys are flattened paths
    /// (<c>switcher/hotkeys</c>); append patches keep their suffix
    /// (<c>schema_list/+</c>). Missing/unparseable files yield an empty list.
    /// </summary>
    public static List<KeyValuePair<string, YamlNode>> ReadFlatPatch(string path)
    {
        var root = RimeYaml.LoadRoot(path);
        if (root is null) return [];

        var result = new List<KeyValuePair<string, YamlNode>>();
        foreach (var (key, value) in root.Children)
        {
            if (key is not YamlScalarNode { Value: { } text }) continue;

            if (text == "patch")
            {
                FlattenMapping(value, "", result);
            }
            else if (text.StartsWith("patch/", StringComparison.Ordinal))
            {
                result.Add(new(text["patch/".Length..], value));
            }
        }
        return result;
    }

    private static void FlattenMapping(YamlNode? node, string prefix, List<KeyValuePair<string, YamlNode>> result)
    {
        if (node is not YamlMappingNode map) return;
        foreach (var (k, v) in map.Children)
        {
            if (k is not YamlScalarNode { Value: { } key }) continue;
            result.Add(new(prefix.Length == 0 ? key : $"{prefix}/{key}", v));
        }
    }

    /// <summary>
    /// Writes <paramref name="patch"/> keys (flatten-path → CLR value) into
    /// the file. Other keys, comments and formatting are preserved.
    /// </summary>
    public static void Write(string path, string fileTitle, IReadOnlyDictionary<string, object?> patch)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (!File.Exists(path))
        {
            File.WriteAllText(path, RenderFresh(fileTitle, patch));
            VerifyOrRollback(path, patch, []);
            return;
        }

        var original = File.ReadAllText(path);
        File.WriteAllText(path + BackupSuffix, original);

        var edited = TryEditLines(path, original, patch) ?? StructuralRewrite(original, patch);
        File.WriteAllText(path, edited);
        VerifyOrRollback(path, patch, []);
    }

    /// <summary>
    /// Removes keys (flatten-path, optionally with <c>/+</c> suffix) from the
    /// patch. Returns true when the file exists and was processed.
    /// </summary>
    public static bool Remove(string path, string fileTitle, IEnumerable<string> keys)
    {
        if (!File.Exists(path)) return false;

        var keyList = keys.ToList();
        var original = File.ReadAllText(path);
        File.WriteAllText(path + BackupSuffix, original);

        var edited = TryRemoveLines(original, keyList) ?? StructuralRemove(original, keyList);
        File.WriteAllText(path, edited);
        VerifyOrRollback(path, new Dictionary<string, object?>(), keyList);
        return true;
    }

    // ---------- line-level editing ----------

    private static string? TryEditLines(string path, string original, IReadOnlyDictionary<string, object?> patch)
    {
        int patchIdx = FindPatchBlock(original, out var lines, out var eol);
        if (patchIdx == -1)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0) lines.Add("");
            lines.Add("patch:");
            patchIdx = lines.Count - 1;
        }
        else if (patchIdx == -2)
        {
            return null; // "patch:" exists but holds an inline flow value
        }

        if (!RemoveKeyLines(lines, patch.Keys)) return null;

        // Removals above the patch block may have shifted its index.
        patchIdx = lines.FindIndex(l => YamlLines.IndentOf(l) == 0 && YamlLines.IsKeyLine(l, "patch"));

        // Insert right below "patch:"; add in reverse so the final file order
        // matches the dictionary order.
        int keyIndent = YamlLines.IndentOf(lines[patchIdx]) + 2;
        foreach (var (key, value) in patch.Reverse())
            lines.InsertRange(patchIdx + 1, EntryLines(key, value, keyIndent));
        return YamlLines.Join(lines, eol);
    }

    private static string? TryRemoveLines(string original, IReadOnlyList<string> keys)
    {
        int patchIdx = FindPatchBlock(original, out var lines, out var eol);
        if (patchIdx == -2) return null;

        if (!RemoveKeyLines(lines, keys)) return null;
        return YamlLines.Join(lines, eol);
    }

    /// <summary>
    /// Locates the top-level <c>patch:</c> key line. Returns its index, -1
    /// when absent, or -2 when it exists with an inline value (not editable
    /// line-wise). Outputs the split lines and the file's EOL.
    /// </summary>
    private static int FindPatchBlock(string original, out List<string> lines, out string eol)
    {
        (lines, eol) = YamlLines.SplitLines(original);
        int idx = lines.FindIndex(l => YamlLines.IndentOf(l) == 0 && YamlLines.IsKeyLine(l, "patch"));
        return idx >= 0 && YamlLines.HasInlineValue(lines[idx]) ? -2 : idx;
    }

    /// <summary>
    /// Removes every representation of each key (all three patch shapes,
    /// including <c>/+</c> variants) directly from the lines. Returns false
    /// when a shape-2/1 scan hit an inline-value block we dare not edit.
    /// </summary>
    private static bool RemoveKeyLines(List<string> lines, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            // Shape 3: top-level "patch/<key>" (and "patch/<key>/+").
            RemoveTopLevelFlatKeys(lines, key);

            int patchIdx = lines.FindIndex(l => YamlLines.IndentOf(l) == 0 && YamlLines.IsKeyLine(l, "patch"));
            if (patchIdx < 0) continue;
            if (YamlLines.HasInlineValue(lines[patchIdx])) return false;

            // Shape 2: a flat "a/b/c" child of the patch block.
            int flatIdx = FindChildKeyInBlock(lines, patchIdx, key);
            if (flatIdx >= 0)
            {
                RemoveKeyExtent(lines, flatIdx);
                continue;
            }

            // Shape 1: descend "a" → "b" → "c" inside the patch block.
            var segments = key.Split('/');
            if (segments.Length == 1) continue;

            var ancestors = new List<int>();
            int parentIdx = patchIdx;
            bool descended = true;
            for (int i = 0; i < segments.Length; i++)
            {
                int childIdx = FindChildKeyInBlock(lines, parentIdx, segments[i]);
                if (childIdx < 0) { descended = false; break; }
                // An intermediate key with an inline flow value hides its
                // children from line editing — bail out to the structural path.
                if (i < segments.Length - 1 && YamlLines.HasInlineValue(lines[childIdx])) return false;
                ancestors.Add(childIdx);
                parentIdx = childIdx;
            }
            if (descended)
            {
                RemoveKeyExtent(lines, ancestors[^1]);
                // Prune now-empty intermediate mappings (keep "patch:" itself).
                for (int i = ancestors.Count - 2; i >= 0; i--)
                    if (BlockIsEmpty(lines, ancestors[i])) RemoveKeyExtent(lines, ancestors[i]);
            }
        }
        return true;
    }

    private static void RemoveTopLevelFlatKeys(List<string> lines, string key)
    {
        foreach (var flat in (string[])[$"patch/{key}", $"patch/{key}/+"])
        {
            for (int i = lines.Count - 1; i >= 0; i--)
            {
                if (YamlLines.IndentOf(lines[i]) == 0 && YamlLines.IsKeyLine(lines[i], flat))
                    RemoveKeyExtent(lines, i);
            }
        }
    }

    /// <summary>Finds a direct child key inside the block started at <paramref name="parentIdx"/>.</summary>
    private static int FindChildKeyInBlock(List<string> lines, int parentIdx, string key)
    {
        int parentIndent = YamlLines.IndentOf(lines[parentIdx]);
        for (int i = parentIdx + 1; i < lines.Count; i++)
        {
            var line = lines[i];
            if (YamlLines.IsBlankOrComment(line)) continue;
            int indent = YamlLines.IndentOf(line);
            if (indent <= parentIndent) return -1; // parent block ended
            if (YamlLines.IsKeyLine(line, key)) return i;
        }
        return -1;
    }

    /// <summary>
    /// Removes the key line at <paramref name="keyIdx"/> together with its
    /// value block, keeping trailing blank/comment lines.
    /// </summary>
    private static void RemoveKeyExtent(List<string> lines, int keyIdx)
    {
        int end = ValueExtentEnd(lines, keyIdx);
        lines.RemoveRange(keyIdx, end - keyIdx);
    }

    /// <summary>End (exclusive) of the value extent of the key line, including the key line itself.</summary>
    private static int ValueExtentEnd(List<string> lines, int keyIdx)
    {
        int keyIndent = YamlLines.IndentOf(lines[keyIdx]);
        int end = keyIdx + 1;
        while (end < lines.Count)
        {
            var trimmed = lines[end].Trim();
            if (trimmed.Length == 0 || trimmed.StartsWith('#')) { end++; continue; }
            int indent = YamlLines.IndentOf(lines[end]);
            if (indent <= keyIndent && !(indent == keyIndent && trimmed.StartsWith("- "))) break;
            end++;
        }
        while (end > keyIdx + 1 && YamlLines.IsBlankOrComment(lines[end - 1])) end--;
        return end;
    }

    private static bool BlockIsEmpty(List<string> lines, int keyIdx)
    {
        int end = ValueExtentEnd(lines, keyIdx);
        for (int i = keyIdx + 1; i < end; i++)
            if (!YamlLines.IsBlankOrComment(lines[i])) return false;
        return true;
    }

    private static List<string> EntryLines(string key, object? value, int keyIndent)
    {
        var (inline, block) = PatchYaml.RenderEntry(value, keyIndent);
        var keyText = $"{PatchYaml.KeyText(key)}:";
        List<string> lines = inline is { } inlineValue
            ? [$"{Spaces(keyIndent)}{keyText} {inlineValue}"]
            : [$"{Spaces(keyIndent)}{keyText}", .. block!];
        return lines;
    }

    // ---------- structural fallback (exotic shapes; loses comments, .bak exists) ----------

    private static string StructuralRewrite(string original, IReadOnlyDictionary<string, object?> patch)
    {
        var root = ParseMapping(original);
        var patchMap = EnsurePatchMapping(root);
        RemoveFromParsed(patchMap, root, patch.Keys);
        foreach (var (key, value) in patch)
            AddFlatKey(patchMap, key, ToYamlNode(value));
        return Serialize(root);
    }

    private static string StructuralRemove(string original, IReadOnlyList<string> keys)
    {
        var root = ParseMapping(original);
        var patchMap = EnsurePatchMapping(root);
        RemoveFromParsed(patchMap, root, keys);
        return Serialize(root);
    }

    private static YamlMappingNode ParseMapping(string original)
    {
        var stream = new YamlStream();
        stream.Load(new StringReader(original));
        if (stream.Documents is not [var document, ..] || document.RootNode is not YamlMappingNode root)
            throw new InvalidOperationException("不是可识别的 YAML 映射，无法安全写入。请手工检查该文件。");
        return root;
    }

    private static YamlMappingNode EnsurePatchMapping(YamlMappingNode root)
    {
        if (RimeYaml.TryGetChild(root, "patch", out var patchNode) && patchNode is YamlMappingNode patch)
            return patch;
        patch = new YamlMappingNode();
        root.Children.Add(new YamlScalarNode("patch"), patch);
        return patch;
    }

    private static void RemoveFromParsed(YamlMappingNode patchMap, YamlMappingNode root, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            foreach (var flat in (string[])[$"patch/{key}", $"patch/{key}/+"])
                root.Children.Remove(new YamlScalarNode(flat));

            patchMap.Children.Remove(new YamlScalarNode(key));
            patchMap.Children.Remove(new YamlScalarNode(key + "/+"));

            // Nested shape: descend "a" → "b" → "c", prune empty parents.
            var segments = key.Split('/');
            if (segments.Length == 1) continue;

            YamlMappingNode? current = patchMap;
            var chain = new List<YamlMappingNode>();
            foreach (var segment in segments[..^1])
            {
                if (current is null) break;
                chain.Add(current);
                current = RimeYaml.TryGetChild(current, segment, out var child) && child is YamlMappingNode map
                    ? map
                    : null;
            }
            if (current is { } leaf)
            {
                leaf.Children.Remove(new YamlScalarNode(segments[^1]));
                for (int i = chain.Count - 1; i >= 0; i--)
                {
                    if (chain[i].Children.Count > 0) break;
                    int parentIndex = i - 1;
                    if (parentIndex < 0) break;
                    chain[parentIndex].Children.Remove(new YamlScalarNode(segments[parentIndex]));
                }
            }
        }
    }

    private static void AddFlatKey(YamlMappingNode patchMap, string key, YamlNode value)
    {
        var keyNode = new YamlScalarNode(key);
        if (PatchYaml.KeyText(key) != key) keyNode.Style = YamlDotNet.Core.ScalarStyle.DoubleQuoted;
        patchMap.Children.Add(keyNode, value);
    }

    private static YamlNode ToYamlNode(object? value) => value switch
    {
        null => new YamlScalarNode((string?)null),
        bool b => new YamlScalarNode(b ? "true" : "false"),
        long or int => new YamlScalarNode(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)),
        double d => new YamlScalarNode(d.ToString(System.Globalization.CultureInfo.InvariantCulture)),
        string s => new YamlScalarNode(s),
        IReadOnlyList<object?> list => new YamlSequenceNode(list.Select(ToYamlNode)),
        IReadOnlyDictionary<string, object?> map => new YamlMappingNode(
            map.Select(kv => new KeyValuePair<YamlNode, YamlNode>(
                new YamlScalarNode(kv.Key), ToYamlNode(kv.Value)))),
        _ => throw new NotSupportedException($"不支持的补丁值类型：{value.GetType().Name}"),
    };

    private static string Serialize(YamlMappingNode root)
    {
        var stream = new YamlStream();
        stream.Documents.Add(new YamlDocument(root));
        using var writer = new StringWriter();
        stream.Save(writer);
        return writer.ToString();
    }

    // ---------- fresh file & verification ----------

    private static string RenderFresh(string fileTitle, IReadOnlyDictionary<string, object?> patch)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"# {fileTitle} —— RIME 用户补丁配置");
        sb.AppendLine("# 部署时会覆盖同名默认设置；修改后需重新部署生效。");
        sb.AppendLine("# 除 patch 下的键外，其余内容可手工编辑；「尔雅」写入时只改动 patch 中对应的键。");
        sb.AppendLine();
        sb.AppendLine("patch:");
        foreach (var (key, value) in patch)
            foreach (var line in EntryLines(key, value, 2))
                sb.AppendLine(line);
        return sb.ToString();
    }

    private static void VerifyOrRollback(string path, IReadOnlyDictionary<string, object?> expected, IReadOnlyList<string> absent)
    {
        if (Verify(path, expected, absent)) return;
        var backupPath = path + BackupSuffix;
        if (File.Exists(backupPath)) File.WriteAllText(path, File.ReadAllText(backupPath));
        throw new InvalidOperationException(
            $"写入 {Path.GetFileName(path)} 后未能通过校验，原文件已还原。");
    }

    private static bool Verify(string path, IReadOnlyDictionary<string, object?> expected, IReadOnlyList<string> absent)
    {
        var flat = ReadFlatPatch(path);
        var values = flat.ToDictionary(kv => kv.Key, kv => PatchYaml.ToClrValue(kv.Value));

        foreach (var (key, value) in expected)
            if (!values.TryGetValue(key, out var actual) || !DeepEquals(actual, value))
                return false;

        foreach (var key in absent)
            if (values.ContainsKey(key)) return false;

        return true;
    }

    private static bool DeepEquals(object? a, object? b)
    {
        if (a is null || b is null) return a is null && b is null;
        if (a is IReadOnlyList<object?> la && b is IReadOnlyList<object?> lb)
            return la.Count == lb.Count && la.Zip(lb).All(p => DeepEquals(p.First, p.Second));
        if (a is IReadOnlyDictionary<string, object?> da && b is IReadOnlyDictionary<string, object?> db)
            return da.Count == db.Count
                && da.All(kv => db.TryGetValue(kv.Key, out var other) && DeepEquals(kv.Value, other));
        // Boxed numbers from different call sites (int vs long vs double).
        if (a.GetType() != b.GetType()
            && a is IConvertible && b is IConvertible
            && IsNumeric(a) && IsNumeric(b))
            return Convert.ToDouble(a, System.Globalization.CultureInfo.InvariantCulture)
                == Convert.ToDouble(b, System.Globalization.CultureInfo.InvariantCulture);
        return a.Equals(b);
    }

    private static bool IsNumeric(object value)
        => value is int or long or double or float or decimal or short or byte or sbyte or uint or ulong or ushort;

    private static string Spaces(int count) => new(' ', count);
}
