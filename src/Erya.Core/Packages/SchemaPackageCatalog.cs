using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Erya.Core.Localization;

namespace Erya.Core.Packages;

/// <summary>
/// The built-in catalog of installable community packages, embedded as JSON
/// so new entries (or upstream branch changes) ship as a one-file edit.
/// </summary>
public static class SchemaPackageCatalog
{

    public static IReadOnlyList<SchemaPackage> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "Erya.Core.Packages.packages.catalog.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"内置方案包目录缺失：{resourceName}");
        var packages = JsonSerializer.Deserialize(stream, SchemaCatalogContext.Default.SchemaPackageArray)
            ?? throw new InvalidOperationException($"内置方案包目录无法解析：{resourceName}");
        Validate(packages, resourceName);
        return ApplyLanguageOverlay(packages);
    }

    /// <summary>
    /// 语言变体 overlay：packages.catalog.{lang}.json 为 { id: 描述 } 的扁平表，
    /// 只覆盖 description（name 是方案品牌名，不翻译）。
    /// </summary>
    private static IReadOnlyList<SchemaPackage> ApplyLanguageOverlay(SchemaPackage[] packages)
    {
        if (CoreLoc.Language.Length == 0) return packages;
        var map = CoreLoc.TryLoadMap($"Erya.Core.Packages.packages.catalog.{CoreLoc.Language}.json");
        if (map is null) return packages;
        return packages.Select(p => map.TryGetValue(p.Id, out var description) ? p with { Description = description } : p)
            .ToArray();
    }

    /// <summary>Fail fast on catalog typos — a bad entry would install nothing.</summary>
    private static void Validate(SchemaPackage[] packages, string resourceName)
    {
        if (packages.Length == 0)
            throw new InvalidOperationException($"{resourceName}：目录为空。");

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var package in packages)
        {
            foreach (var (field, value) in new[]
                     {
                         ("id", package.Id), ("name", package.Name),
                         ("owner", package.Owner), ("repo", package.Repo), ("branch", package.Branch),
                     })
                if (string.IsNullOrWhiteSpace(value))
                    throw new InvalidOperationException($"{resourceName}：条目 {package.Id} 缺少 {field}。");
            if (!package.ArchiveUrl.StartsWith("https://codeload.github.com/", StringComparison.Ordinal))
                throw new InvalidOperationException($"{resourceName}：条目 {package.Id} 的下载地址异常：{package.ArchiveUrl}");
            if (!ids.Add(package.Id))
                throw new InvalidOperationException($"{resourceName}：条目 id 重复：{package.Id}。");
            if (package.MarkerSchemas.Length == 0 || package.MarkerSchemas.Any(m => !m.EndsWith(".schema.yaml", StringComparison.Ordinal)))
                throw new InvalidOperationException($"{resourceName}：条目 {package.Id} 缺少有效的 markerSchemas（须为 .schema.yaml 文件名）。");
        }
    }
}

/// <summary>Native AOT 下 System.Text.Json 反射模式不可用，内置目录走源生成。</summary>
[JsonSerializable(typeof(SchemaPackage[]))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
internal sealed partial class SchemaCatalogContext : JsonSerializerContext;
