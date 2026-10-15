using YamlDotNet.RepresentationModel;

namespace Erya.Core.Configuration;

/// <summary>
/// Overlays a <c>.custom.yaml</c> patch onto a base config mapping,
/// producing the tree RIME effectively deploys. Mirrors librime's patch
/// semantics for the common cases: plain keys replace, <c>key/+</c> appends
/// to a sequence, and intermediate missing mappings are created.
/// </summary>
internal static class MergedConfig
{
    /// <summary>Applies <paramref name="flatPatch"/> onto <paramref name="root"/> in place.</summary>
    public static void Apply(YamlMappingNode root, IEnumerable<KeyValuePair<string, YamlNode>> flatPatch)
    {
        foreach (var (key, value) in flatPatch)
        {
            bool append = key.EndsWith("/+", StringComparison.Ordinal);
            var path = append ? key[..^2] : key;
            var segments = path.Split('/');
            if (segments.Any(string.IsNullOrEmpty)) continue;

            YamlMappingNode current = root;
            foreach (var segment in segments[..^1])
            {
                if (RimeYaml.TryGetChild(current, segment, out var child) && child is YamlMappingNode childMap)
                {
                    current = childMap;
                }
                else
                {
                    var map = new YamlMappingNode();
                    SetOrReplace(current, segment, map);
                    current = map;
                }
            }

            var leaf = segments[^1];
            if (append)
            {
                var sequence = RimeYaml.TryGetChild(current, leaf, out var existing) && existing is YamlSequenceNode seq
                    ? seq
                    : new YamlSequenceNode();
                if (value is YamlSequenceNode additions)
                    foreach (var item in additions) sequence.Add(item);
                else
                    sequence.Add(value);
                SetOrReplace(current, leaf, sequence);
            }
            else
            {
                SetOrReplace(current, leaf, value);
            }
        }
    }

    /// <summary>Looks a flatten path (<c>switcher/hotkeys</c>) up in a merged tree.</summary>
    public static YamlNode? TryGetByPath(YamlMappingNode root, string path)
    {
        YamlNode current = root;
        foreach (var segment in path.Split('/'))
        {
            if (current is not YamlMappingNode map || !RimeYaml.TryGetChild(map, segment, out var child))
                return null;
            current = child;
        }
        return current;
    }

    private static void SetOrReplace(YamlMappingNode map, string key, YamlNode value)
    {
        map.Children.Remove(new YamlScalarNode(key));
        map.Children.Add(new YamlScalarNode(key), value);
    }
}
