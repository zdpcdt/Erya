using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Erya.Core.Settings;

/// <summary>
/// Loads the embedded settings schemas and resolves which schema a
/// distribution's config pages should use — the cross-platform seam:
/// <c>default.yaml</c> settings apply to every frontend, the looks schema
/// follows the frontend's own YAML (squirrel/weasel), and Linux frontends
/// have none.
/// </summary>
public static class SettingsSchemas
{

    /// <summary>Core engine settings — every RIME frontend reads default.yaml.</summary>
    public static SettingsSchema LoadDefault()
        => Load("default");

    /// <summary>
    /// Looks/behavior schema for the frontend's own YAML, selected by the
    /// distribution's <c>UiConfigFile</c> (squirrel.yaml → squirrel schema,
    /// weasel.yaml → weasel schema). Throws for other file names.
    /// </summary>
    public static SettingsSchema LoadForUiConfig(string uiConfigFile)
        => Load(Path.GetFileNameWithoutExtension(uiConfigFile));

    private static SettingsSchema Load(string name)
    {
        var assembly = Assembly.GetExecutingAssembly();
        var resourceName = $"Erya.Core.Settings.settings.{name}.json";
        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidOperationException($"内置设置 schema 缺失：{resourceName}");
        var schema = JsonSerializer.Deserialize(stream, SettingsSchemaContext.Default.SettingsSchema)
            ?? throw new InvalidOperationException($"内置设置 schema 无法解析：{resourceName}");
        Validate(schema, resourceName);
        return schema;
    }

    /// <summary>Fail fast on schema typos — a bad field would render as nothing.</summary>
    private static void Validate(SettingsSchema schema, string resourceName)
    {
        if (string.IsNullOrEmpty(schema.TargetFile))
            throw new InvalidOperationException($"{resourceName}: 缺少 targetFile。");

        foreach (var section in schema.Sections)
        {
            if (string.IsNullOrWhiteSpace(section.Title))
                throw new InvalidOperationException($"{resourceName}: 小节缺少 title。");
            foreach (var field in section.Fields)
            {
                if (!SettingsSchema.KnownFieldTypes.Contains(field.Type))
                    throw new InvalidOperationException(
                        $"{resourceName}: 字段 {section.Id}/{field.Id} 使用未知控件类型 {field.Type}。");
                if (string.IsNullOrEmpty(field.KeyPath))
                    throw new InvalidOperationException(
                        $"{resourceName}: 字段 {section.Id}/{field.Id} 缺少 keyPath。");
                if (field.Min is { } min && field.Max is { } max && min > max)
                    throw new InvalidOperationException(
                        $"{resourceName}: 字段 {section.Id}/{field.Id} 的 min 大于 max。");
            }
        }
    }
}

/// <summary>Native AOT 下 System.Text.Json 反射模式不可用，内置 schema 走源生成。</summary>
[JsonSerializable(typeof(SettingsSchema))]
[JsonSourceGenerationOptions(
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true)]
internal sealed partial class SettingsSchemaContext : JsonSerializerContext;
