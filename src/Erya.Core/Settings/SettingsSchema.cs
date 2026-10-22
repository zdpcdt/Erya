using System.Text.Json;

namespace Erya.Core.Settings;

/// <summary>
/// One settings page: an ordered list of sections, each rendering fields
/// against one base config file. Schemas ship as embedded JSON modeled on
/// SCT's ConfigSchema.json — new RIME config keys are added by editing JSON,
/// not code.
/// </summary>
public sealed class SettingsSchema
{
    /// <summary>Base file the sections patch, e.g. "default.yaml" / "squirrel.yaml".</summary>
    public string TargetFile { get; set; } = "";

    public List<SettingsSection> Sections { get; set; } = [];

    /// <summary>Field types the schema loader and UI know how to render.</summary>
    public static readonly IReadOnlySet<string> KnownFieldTypes = new HashSet<string>
    {
        "toggle", "stepper", "slider", "segmented", "enum", "text", "font",
        "hotkey", "hotkeyList", "hotkeyPairList", "keyMapping", "checkList", "appOptions",
    };
}

public sealed class SettingsSection
{
    public string Id { get; set; } = "";

    public string Title { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>
    /// 页面归属（外观目标专用）：skin=皮肤库页 / layout=候选窗排版页 / apps=应用行为页。
    /// null = 无分组（单页全显，default 目标的输入设置）。
    /// </summary>
    public string? Group { get; set; }

    public List<SettingsField> Fields { get; set; } = [];
}

public sealed class SettingsField
{
    public string Id { get; set; } = "";

    public string Label { get; set; } = "";

    public string? Description { get; set; }

    /// <summary>One of <see cref="SettingsSchema.KnownFieldTypes"/>.</summary>
    public string Type { get; set; } = "";

    /// <summary>Flatten path in the base file, e.g. "switcher/hotkeys".</summary>
    public string KeyPath { get; set; } = "";

    public double? Min { get; set; }

    public double? Max { get; set; }

    public double? Step { get; set; }

    /// <summary>Static choices for enum/segmented/checkList/keyMapping value lists.</summary>
    public List<string>? Choices { get; set; }

    /// <summary>Path in the merged config whose children keys become choices (color schemes).</summary>
    public string? ChoicesRef { get; set; }

    /// <summary>Value shown when neither base nor patch carries one (JSON-native).</summary>
    public JsonElement Default { get; set; }

    /// <summary>Row keys for keyMapping fields (e.g. Caps_Lock, Shift_L …).</summary>
    public List<string>? Keys { get; set; }

    /// <summary>Per-item labels for fixed-size pair lists (e.g. 上页/下页).</summary>
    public List<string>? ItemLabels { get; set; }

    /// <summary>
    /// 行首图标，形如 "td:Keyboard"（Irihi.Iconica.TDesign）、"ip:UpOne"
    /// （Irihi.Iconica.IconPark）、"semi:SemiIconFont"（Semi 图标资源键）。
    /// 按字段语义挑选，宁缺勿滥；缺失时不显示图标（行内仍占位保持对齐）。
    /// </summary>
    public string? Icon { get; set; }

    /// <summary>CLR form of <see cref="Default"/> (null when absent or JSON null).</summary>
    public object? DefaultValue() => Default.ValueKind switch
    {
        JsonValueKind.Number => Default.TryGetInt64(out var l) ? (object)l : Default.GetDouble(),
        JsonValueKind.String => Default.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Array => Default.EnumerateArray().Select(Item).ToList(),
        _ => null,
    };

    private static object? Item(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.Number => e.TryGetInt64(out var l) ? (object)l : e.GetDouble(),
        JsonValueKind.String => e.GetString(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };
}
