using Erya.Core.Settings;

namespace Erya.Core.Tests;

/// <summary>
/// The embedded settings schemas are code-driven UI: a typo would silently
/// drop a control, so their shape is pinned by tests.
/// </summary>
public sealed class SettingsSchemaTests
{
    [Fact]
    public void Default_schema_targets_default_yaml_and_core_sections()
    {
        var schema = SettingsSchemas.LoadDefault();

        Assert.Equal("default.yaml", schema.TargetFile);
        Assert.Equal(
            ["menu", "switcher", "asciiComposer", "keyBinder", "punctuator"],
            schema.Sections.Select(s => s.Id).ToList());

        var pageSize = Field(schema, "menu", "pageSize");
        Assert.Equal((3d, 10d, 1d), (pageSize.Min, pageSize.Max, pageSize.Step));
        Assert.Equal(5L, pageSize.DefaultValue());
    }

    [Fact]
    public void Default_schema_switch_key_maps_all_modifier_keys()
    {
        var schema = SettingsSchemas.LoadDefault();
        var switchKey = Field(schema, "asciiComposer", "switchKey");

        Assert.Equal(["Caps_Lock", "Shift_L", "Shift_R", "Control_L", "Control_R"], switchKey.Keys);
        Assert.Contains("inline_ascii", switchKey.Choices!);
    }

    [Fact]
    public void Squirrel_schema_targets_squirrel_yaml_with_dynamic_color_schemes()
    {
        var schema = SettingsSchemas.LoadForUiConfig("squirrel.yaml");

        Assert.Equal("squirrel.yaml", schema.TargetFile);
        var light = Field(schema, "style", "colorScheme");
        Assert.Equal("preset_color_schemes", light.ChoicesRef);
        Assert.Null(light.Choices);

        // Per-app behavior is part of the looks schema (bundle IDs).
        Assert.Contains(schema.Sections, s => s.Id == "appOptions");
    }

    [Fact]
    public void Squirrel_schema_carries_the_layout_keys_squirrel_style_reads()
    {
        // 鼠须管把这些排版参数平铺在 style 下（没有 layout 一层）；
        // 与小狼毫同名不同路径，预览与 schema 都要两套键各认各的。
        var schema = SettingsSchemas.LoadForUiConfig("squirrel.yaml");
        var keyPaths = schema.Sections.SelectMany(s => s.Fields).Select(f => f.KeyPath).ToHashSet();

        Assert.Superset(
            new HashSet<string>
            {
                "style/margin_x", "style/margin_y", "style/candidate_spacing",
                "style/hilite_spacing", "style/hilite_padding",
                "style/label_font_face", "style/comment_font_face",
            },
            keyPaths);
    }

    [Fact]
    public void Weasel_schema_key_paths_are_all_read_by_weasel_017()
    {
        var schema = SettingsSchemas.LoadForUiConfig("weasel.yaml");

        Assert.Equal("weasel.yaml", schema.TargetFile);
        // Keys RimeWithWeasel.cpp of weasel 0.17.x actually reads (verified
        // against the 0.17.4 source). A field outside this set renders fine
        // but can never take effect — a pure placebo in the UI.
        var weaselReadKeys = new HashSet<string>
        {
            "style/color_scheme", "style/color_scheme_dark",
            "style/layout/type", // read LAST by weasel — beats horizontal/vertical_text/text_orientation
            "style/inline_preedit", "style/display_tray_icon",
            "style/paging_on_scroll", "style/font_face", "style/font_point",
            "style/label_font_face", "style/label_font_point", "style/label_format",
            "style/mark_text", "style/comment_font_face", "style/comment_font_point",
            "style/hover_type", "style/candidate_abbreviate_length",
            "style/layout/corner_radius", "style/layout/hilited_corner_radius",
            "style/layout/linespacing", "style/layout/spacing",
            "style/layout/candidate_spacing", "style/layout/hilite_spacing",
            "style/layout/hilite_padding", "style/layout/margin_x", "style/layout/margin_y",
            "style/layout/border_width", "style/layout/shadow_radius",
            "style/layout/shadow_offset_x", "style/layout/shadow_offset_y",
            "style/layout/min_width", "style/layout/min_height",
            "style/layout/max_width", "style/layout/max_height",
        };
        foreach (var field in schema.Sections.SelectMany(s => s.Fields))
        {
            if (field.Type == "appOptions") continue; // app_options is read by every frontend
            Assert.True(
                weaselReadKeys.Contains(field.KeyPath),
                $"小狼毫不读取 {field.KeyPath}（字段 {field.Id}）——别把鼠须管的键抄进 weasel schema。");
        }
        // Keys we deliberately do NOT expose on weasel: squirrel-only
        // spellings weasel ignores, plus lower-precedence orientation keys
        // that style/layout/type (read last) silently overrides.
        Assert.DoesNotContain(schema.Sections.SelectMany(s => s.Fields),
            f => f.KeyPath is "style/candidate_list_layout" or "style/translucency"
                 or "style/mutual_exclusive" or "style/alpha" or "style/inline_candidate"
                 or "style/horizontal" or "style/text_orientation" or "style/vertical_text");
        // 排列方式 must map onto the highest-precedence key weasel reads.
        Assert.Contains(schema.Sections.SelectMany(s => s.Fields), f => f.KeyPath == "style/layout/type");
    }

    [Fact]
    public void Font_face_fields_use_the_font_picker_type()
    {
        // The picker reads the machine's installed fonts; a plain text field
        // here would silently lose that behavior.
        foreach (var schema in (SettingsSchema[])[
                     SettingsSchemas.LoadForUiConfig("squirrel.yaml"),
                     SettingsSchemas.LoadForUiConfig("weasel.yaml")])
        {
            var fontFace = schema.Sections.SelectMany(s => s.Fields).Single(f => f.KeyPath == "style/font_face");
            Assert.Equal("font", fontFace.Type);
        }
    }

    [Fact]
    public void Every_field_has_label_type_and_key_path()
    {
        foreach (var schema in (SettingsSchema[])[
                     SettingsSchemas.LoadDefault(),
                     SettingsSchemas.LoadForUiConfig("squirrel.yaml"),
                     SettingsSchemas.LoadForUiConfig("weasel.yaml")])
        {
            Assert.All(
                schema.Sections.SelectMany(s => s.Fields),
                f => Assert.False(
                    string.IsNullOrWhiteSpace(f.Label) || string.IsNullOrWhiteSpace(f.KeyPath),
                    $"字段不完整：{f.Id}"));
        }
    }

    private static SettingsField Field(SettingsSchema schema, string sectionId, string fieldId)
        => schema.Sections.First(s => s.Id == sectionId).Fields.First(f => f.Id == fieldId);
}
