using System.Collections.Frozen;
using Erya.Core.Models;
using YamlDotNet.RepresentationModel;
using static Erya.Core.Configuration.RimeYaml;

namespace Erya.Core.Configuration;

/// <summary>
/// Facade over a RIME user directory: discovers schema files, resolves the
/// effective schema list (<c>default.custom.yaml</c> patch over
/// <c>default.yaml</c>), and persists selections as patches.
/// </summary>
public sealed class RimeSchemaCatalog
{
    /// <summary>Directories scanned for <c>*.schema.yaml</c>; earlier entries win.</summary>
    private readonly string _userDir;
    private readonly IReadOnlyList<string> _additionalSchemaDirs;

    public RimeSchemaCatalog(string userDir, IReadOnlyList<string>? additionalSchemaDirs = null)
    {
        _userDir = userDir;
        _additionalSchemaDirs = additionalSchemaDirs ?? [];
    }

    public string DefaultYamlPath => FindExisting("default.yaml") ?? Path.Combine(_userDir, "default.yaml");

    /// <summary>Currently active schema ids, in activation order.</summary>
    public IReadOnlyList<string> ReadSelectedIds()
    {
        var defaultRoot = LoadRoot(DefaultYamlPath);
        var baseIds = defaultRoot is { } root && TryGetChild(root, "schema_list", out var baseNode)
            ? ReadSchemaList(baseNode)
            : [];

        var customRoot = LoadRoot(Path.Combine(_userDir, DefaultCustomYamlEditor.FileName));
        if (customRoot is null) return baseIds;

        if (TryGetChild(customRoot, "patch", out var patchNode) && patchNode is YamlMappingNode patch)
        {
            if (TryGetChild(patch, "schema_list", out var list))
                return ReadSchemaList(list);
            if (TryGetChild(patch, "schema_list/+", out var appended))
                return MergeAppend(baseIds, ReadSchemaList(appended));
        }
        if (TryGetChild(customRoot, "patch/schema_list", out var flat))
            return ReadSchemaList(flat);
        if (TryGetChild(customRoot, "patch/schema_list/+", out var flatAppended))
            return MergeAppend(baseIds, ReadSchemaList(flatAppended));

        return baseIds;
    }

    public RimeSchemaSnapshot LoadSnapshot()
    {
        var selected = ReadSelectedIds();
        var names = DiscoverSchemaNames();

        List<RimeSchema> schemas = [];
        HashSet<string> seen = new(StringComparer.Ordinal);
        foreach (var id in selected)
        {
            if (!seen.Add(id)) continue;
            schemas.Add(new RimeSchema(id, names.TryGetValue(id, out var name) ? name : id));
        }

        // Schemas shipped on disk but not (yet) enabled, alphabetically after
        // the active ones — those are the candidates the user can tick on.
        var inactive = names.Keys
            .Where(id => !seen.Contains(id))
            .OrderBy(id => names[id], StringComparer.CurrentCulture);
        schemas.AddRange(inactive.Select(id => new RimeSchema(id, names[id])));

        return new RimeSchemaSnapshot(schemas, selected.ToFrozenSet(StringComparer.Ordinal));
    }

    /// <summary>
    /// Persists the selection as a <c>default.custom.yaml</c> patch.
    /// Original <c>default.yaml</c> files are never touched (RIME upgrade rule).
    /// </summary>
    public void WriteSelectedSchemas(IReadOnlyList<string> schemaIds)
        => DefaultCustomYamlEditor.WriteSchemaListPatch(_userDir, schemaIds);

    /// <summary>
    /// Scans <c>*.schema.yaml</c> files across all search directories and maps
    /// schema id → display name (<c>schema/name</c> in the file). The user
    /// directory is scanned first, so a customized local copy shadows the
    /// shipped one.
    /// </summary>
    private Dictionary<string, string> DiscoverSchemaNames()
    {
        Dictionary<string, string> names = new(StringComparer.Ordinal);
        foreach (var dir in SearchDirectories)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.schema.yaml"))
            {
                try
                {
                    var root = LoadRoot(file);
                    if (root is null || !TryGetChild(root, "schema", out var schemaNode)
                        || schemaNode is not YamlMappingNode schema) continue;

                    string id = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(file));
                    if (TryGetChild(schema, "schema_id", out var idNode) && idNode is YamlScalarNode { Value: { } declared })
                        id = declared;
                    if (names.ContainsKey(id)) continue;
                    names[id] = TryGetChild(schema, "name", out var nameNode) && nameNode is YamlScalarNode { Value: { } name }
                        ? name
                        : id;
                }
                catch (Exception ex) when (ex is YamlDotNet.Core.YamlException or IOException)
                {
                    // An unreadable schema file must not break the whole catalog.
                }
            }
        }
        return names;
    }

    private IEnumerable<string> SearchDirectories
    {
        get
        {
            yield return _userDir;
            foreach (var dir in _additionalSchemaDirs) yield return dir;
        }
    }

    private string? FindExisting(string fileName)
        => SearchDirectories.Select(dir => Path.Combine(dir, fileName)).FirstOrDefault(File.Exists);
}
