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
    public void Weasel_schema_uses_the_shared_style_subset_only()
    {
        var schema = SettingsSchemas.LoadForUiConfig("weasel.yaml");

        Assert.Equal("weasel.yaml", schema.TargetFile);
        Assert.DoesNotContain(schema.Sections.SelectMany(s => s.Fields), f => f.Id == "showPaging");
        Assert.DoesNotContain(schema.Sections.SelectMany(s => s.Fields), f => f.Id == "memorizeSize");
        Assert.Contains(schema.Sections.SelectMany(s => s.Fields), f => f.Id == "inlinePreedit");
        // Alpha default parses as an integral JSON number (long).
        Assert.Equal(1L, Field(schema, "style", "alpha").DefaultValue());
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
