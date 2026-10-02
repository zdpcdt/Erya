using YamlDotNet.RepresentationModel;

namespace Erya.Core.Configuration;

/// <summary>
/// Node-level helpers for reading RIME YAML documents.
/// <para>
/// Used only for reading — RIME files must never be rewritten wholesale with
/// YamlDotNet (it drops comments), so writing goes through
/// <see cref="DefaultCustomYamlEditor"/> instead.
/// </para>
/// </summary>
internal static class RimeYaml
{
    /// <summary>
    /// Loads a YAML file and returns its root mapping, or null when the file
    /// is missing, empty or not a mapping (including parse errors — callers
    /// degrade gracefully instead of failing the whole operation).
    /// </summary>
    public static YamlMappingNode? LoadRoot(string? path)
    {
        if (path is null || !File.Exists(path)) return null;
        try
        {
            var stream = new YamlStream();
            stream.Load(new StringReader(File.ReadAllText(path)));
            return stream.Documents is [_, ..] && stream.Documents[0].RootNode is YamlMappingNode root
                ? root
                : null;
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or IOException)
        {
            return null;
        }
    }

    /// <summary>Child lookup by scalar key text (ignoring quoting style).</summary>
    public static bool TryGetChild(YamlMappingNode map, string key, out YamlNode value)
    {
        foreach (var (k, v) in map.Children)
        {
            if (k is YamlScalarNode { Value: { } text } && string.Equals(text, key, StringComparison.Ordinal))
            {
                value = v;
                return true;
            }
        }
        value = null!;
        return false;
    }

    /// <summary>
    /// Reads an ordered list of schema ids from a <c>schema_list</c> node.
    /// Entries are either <c>{ schema: id }</c> maps (block or flow) or plain
    /// scalars; anything else is skipped.
    /// </summary>
    public static IReadOnlyList<string> ReadSchemaList(YamlNode? node)
    {
        if (node is not YamlSequenceNode sequence) return [];
        List<string> ids = [];
        foreach (var item in sequence)
        {
            string? id = item switch
            {
                YamlMappingNode map when TryGetChild(map, "schema", out var v) && v is YamlScalarNode { Value: { } s } => s,
                YamlScalarNode { Value: { } scalar } => scalar,
                _ => null,
            };
            if (!string.IsNullOrWhiteSpace(id)) ids.Add(id);
        }
        return ids;
    }

    /// <summary>Appends <paramref name="appendedIds"/> to <paramref name="baseIds"/> without duplicates.</summary>
    public static IReadOnlyList<string> MergeAppend(IReadOnlyList<string> baseIds, IReadOnlyList<string> appendedIds)
    {
        var merged = new List<string>(baseIds);
        foreach (var id in appendedIds)
            if (!merged.Contains(id, StringComparer.Ordinal))
                merged.Add(id);
        return merged;
    }
}
