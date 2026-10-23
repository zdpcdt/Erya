using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using Erya.Core.Localization;

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
        ApplyLanguageOverlay(schema, name);
        return schema;
    }

    /// <summary>
    /// 语言变体 overlay：settings.{name}.{lang}.json 为扁平键值表
    /// （"sectionId.title" / "sectionId.fieldId.label" / "…description"），
    /// 只覆盖文案，结构（keyPath/type/default）始终来自 base 文件。
    /// </summary>
    private static void ApplyLanguageOverlay(SettingsSchema schema, string name)
    {
        if (CoreLoc.Language.Length == 0) return;
        var map = CoreLoc.TryLoadMap($"Erya.Core.Settings.settings.{name}.{CoreLoc.Language}.json");
        if (map is null) return;

        foreach (var section in schema.Sections)
        {
            if (map.TryGetValue($"{section.Id}.title", out var title) && title.Length > 0)
                section.Title = title;
            if (map.TryGetValue($"{section.Id}.description", out var description) && description.Length > 0)
                section.Description = description;
            foreach (var field in section.Fields)
            {
                if (map.TryGetValue($"{section.Id}.{field.Id}.label", out var label) && label.Length > 0)
                    field.Label = label;
                if (map.TryGetValue($"{section.Id}.{field.Id}.description", out var fieldDescription)
                    && fieldDescription.Length > 0)
                    field.Description = fieldDescription;
            }
        }
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
