using Erya.Core.Localization;
using YamlDotNet.RepresentationModel;

namespace Erya.Core.Configuration;

/// <summary>
/// Facade over one RIME user directory for reading and patching the YAML
/// config family of a base file (<c>default.yaml</c>, <c>squirrel.yaml</c>,
/// <c>weasel.yaml</c> …). Enforces the RIME upgrade rule: base files are
/// never written — every change lands in the sibling
/// <c>&lt;name&gt;.custom.yaml</c> patch.
/// <para>
/// Base lookups prefer the user directory; files shipped by the
/// distribution (shared data dirs) serve as fallback, so a user's local
/// <c>default.yaml</c> shadows the shipped one exactly as librime resolves it.
/// </para>
/// </summary>
public sealed class RimeConfigStore
{
    private readonly IReadOnlyList<string> _searchDirs;

    public RimeConfigStore(string userDir, IReadOnlyList<string>? sharedDirs = null)
    {
        UserDir = userDir;
        _searchDirs = [userDir, .. sharedDirs ?? []];
    }

    public string UserDir { get; }

    /// <summary>Path of the patch file for a base file name, whether or not it exists.</summary>
    public string CustomPath(string baseFileName)
        => Path.Combine(UserDir, Path.ChangeExtension(baseFileName, ".custom.yaml"));

    /// <summary>Path of the base file actually used (user dir shadows shared dirs), or the user-dir candidate.</summary>
    public string BasePath(string baseFileName)
        => _searchDirs.Select(dir => Path.Combine(dir, baseFileName)).FirstOrDefault(File.Exists)
           ?? Path.Combine(UserDir, baseFileName);

    public bool CustomFileExists(string baseFileName) => File.Exists(CustomPath(baseFileName));

    /// <summary>
    /// The effective config tree: base file overlaid with the user patch.
    /// Null when neither file exists or neither parses.
    /// </summary>
    public YamlMappingNode? LoadMerged(string baseFileName)
    {
        var root = RimeYaml.LoadRoot(BasePath(baseFileName));
        var customRoot = RimeYaml.LoadRoot(CustomPath(baseFileName));
        if (customRoot is null) return root;

        root ??= new YamlMappingNode();
        MergedConfig.Apply(root, ResolveIncludes(baseFileName, CustomYamlPatcher.ReadFlatPatch(CustomPath(baseFileName))));
        return root;
    }

    /// <summary>
    /// The base file's tree without any patch overlay — what the distribution
    /// shipped and what a removed patch entry falls back to.
    /// </summary>
    public YamlMappingNode? LoadBase(string baseFileName)
        => RimeYaml.LoadRoot(BasePath(baseFileName));

    /// <summary>
    /// Expands <c>__include: "other:/node"</c> patch entries (the way tools
    /// like rimetool share one hot_keys.yaml across frontends) into their
    /// referenced entries, applied before the file's own keys so explicit
    /// local patches win — mirroring librime. Unresolvable includes are
    /// dropped from the overlay rather than applied as a literal key.
    /// </summary>
    internal IEnumerable<KeyValuePair<string, YamlNode>> ResolveIncludes(
        string baseFileName, IEnumerable<KeyValuePair<string, YamlNode>> flatPatch)
    {
        var included = new List<KeyValuePair<string, YamlNode>>();
        foreach (var (key, value) in flatPatch)
        {
            if (key != "__include"
                || value is not YamlScalarNode { Value: { } target }
                || target.Split(':', 2) is not [var resourceName, var nodePath])
            {
                continue;
            }

            var resourceRoot = baseFileName == resourceName + ".yaml"
                ? null // self-include: nothing outside the file to pull in
                : RimeYaml.LoadRoot(_searchDirs
                    .Select(dir => Path.Combine(dir, resourceName + ".yaml"))
                    .FirstOrDefault(File.Exists)
                    ?? Path.Combine(UserDir, resourceName + ".yaml"));
            YamlNode? node = resourceRoot;
            foreach (var segment in nodePath.Split('/', StringSplitOptions.RemoveEmptyEntries))
            {
                if (node is not YamlMappingNode segmentMap || !RimeYaml.TryGetChild(segmentMap, segment, out var child))
                {
                    node = null;
                    break;
                }
                node = child;
            }
            if (node is not YamlMappingNode includedMap) continue;

            foreach (var (childKey, childValue) in includedMap.Children)
                if (childKey is YamlScalarNode { Value: { } includedKey })
                    included.Add(new(includedKey, childValue));
        }

        return included.Concat(flatPatch.Where(kv => kv.Key != "__include"));
    }

    /// <summary>
    /// Reads a value from the merged tree; null when the path is absent
    /// (callers fall back to the schema default).
    /// </summary>
    public object? ReadMergedValue(string baseFileName, string keyPath)
    {
        var merged = LoadMerged(baseFileName);
        return merged is { } root ? PatchYaml.ToClrValue(MergedConfig.TryGetByPath(root, keyPath)) : null;
    }

    /// <summary>The raw patch entries in file order (keys may carry <c>/+</c> suffixes).</summary>
    public IReadOnlyList<KeyValuePair<string, YamlNode>> ReadPatch(string baseFileName)
        => CustomYamlPatcher.ReadFlatPatch(CustomPath(baseFileName));

    /// <summary>Patch entries as CLR values, keyed by flatten path — what the settings UI diffs against.</summary>
    public IReadOnlyDictionary<string, object?> ReadPatchValues(string baseFileName)
        => CustomYamlPatcher.ReadFlatPatch(CustomPath(baseFileName))
            .GroupBy(kv => kv.Key) // last shape wins, matching librime's merge order
            .ToDictionary(g => g.Key, g => PatchYaml.ToClrValue(g.Last().Value));

    /// <summary>Mapping keys under a merged path (e.g. color scheme names under preset_color_schemes).</summary>
    public IReadOnlyList<string> ListMappingKeys(string baseFileName, string keyPath)
    {
        var merged = LoadMerged(baseFileName);
        return merged is { } root && MergedConfig.TryGetByPath(root, keyPath) is YamlMappingNode map
            ? map.Children.Keys
                .OfType<YamlScalarNode>()
                .Select(k => k.Value)
                .Where(v => v is not null)
                .Select(v => v!)
                .ToList()
            : [];
    }

    /// <summary>Writes keys (flatten-path → CLR value) into the patch file.</summary>
    public void WritePatch(string baseFileName, IReadOnlyDictionary<string, object?> patch)
        => CustomYamlPatcher.Write(CustomPath(baseFileName), Path.GetFileName(CustomPath(baseFileName)), patch);

    /// <summary>Removes keys from the patch file; true when the file existed and was processed.</summary>
    public bool RemovePatchKeys(string baseFileName, IEnumerable<string> keys)
        => CustomYamlPatcher.Remove(CustomPath(baseFileName), Path.GetFileName(CustomPath(baseFileName)), keys);

    /// <summary>Current text of the patch file ("" when missing) — source-editor material.</summary>
    public string ReadCustomText(string baseFileName)
        => File.Exists(CustomPath(baseFileName)) ? File.ReadAllText(CustomPath(baseFileName)) : "";

    /// <summary>
    /// Replaces the patch file wholesale (source mode). The text must parse as
    /// a YAML mapping — anything else is refused; the previous file is kept
    /// as <c>.bak</c>.
    /// </summary>
    public void WriteCustomText(string baseFileName, string text)
    {
        var stream = new YamlStream();
        try
        {
            stream.Load(new StringReader(text));
        }
        catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or ArgumentException)
        {
            throw new InvalidOperationException(CoreLoc.Format("yaml_parseFailed", ex.Message), ex);
        }
        if (stream.Documents is [var document, ..] && document.RootNode is not YamlMappingNode)
            throw new InvalidOperationException(CoreLoc.Get("yaml_patchNotMapping"));

        var path = CustomPath(baseFileName);
        Directory.CreateDirectory(UserDir);
        if (File.Exists(path)) File.WriteAllText(path + ".bak", File.ReadAllText(path));
        File.WriteAllText(path, text);
    }
}
