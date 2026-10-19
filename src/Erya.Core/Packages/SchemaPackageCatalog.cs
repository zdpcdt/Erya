using System.Reflection;
using System.Text.Json;

namespace Erya.Core.Packages;

/// <summary>
/// The built-in catalog of installable community packages, embedded as JSON
/// so new entries (or upstream branch changes) ship as a one-file edit.
/// </summary>
public static class SchemaPackageCatalog
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static IReadOnlyList<SchemaPackage> Load()
    {
        var assembly = Assembly.GetExecutingAssembly();
        const string resourceName = "Erya.Core.Packages.packages.catalog.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"内置方案包目录缺失：{resourceName}");
        var packages = JsonSerializer.Deserialize<SchemaPackage[]>(stream, JsonOptions)
            ?? throw new InvalidOperationException($"内置方案包目录无法解析：{resourceName}");
        Validate(packages, resourceName);
        return packages;
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
