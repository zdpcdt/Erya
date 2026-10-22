using System;
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
    public void Default_schema_key_paths_are_all_read_by_librime()
    {
        // 键集对照 rime/librime 1.16 源码逐一核实（注释为读取处）。key_binder
        // 组件只读 key_binder/bindings（key_binder.cc），引擎没有成对快捷键
        // API——select_pair/cursor_pair/page_pair 写进补丁永远不生效，已撤下。
        var schema = SettingsSchemas.LoadDefault();

        var librimeReadKeys = new HashSet<string>
        {
            "menu/page_size",                                             // schema.cc
            "switcher/caption", "switcher/hotkeys",                       // switcher.cc
            "switcher/save_options", "switcher/fold_options",
            "switcher/option_list_separator", "switcher/abbreviate_options", // switch_translator.cc
            "ascii_composer/good_old_caps_lock", "ascii_composer/switch_key", // ascii_composer.cc
            "punctuator/digit_separators", "punctuator/digit_separator_action", // punctuator.cc，≥1.13
        };
        // 引擎不读、由方案附带脚本消费的键：雾凇拼音 lua/select_character.lua
        // 用 config:get_string 读取（标量——控件必须写单键，不能写列表）。
        var schemeScriptKeys = new HashSet<string>
        {
            "key_binder/select_first_character", "key_binder/select_last_character",
        };
        foreach (var field in schema.Sections.SelectMany(s => s.Fields))
        {
            Assert.True(
                librimeReadKeys.Contains(field.KeyPath) || schemeScriptKeys.Contains(field.KeyPath),
                $"librime 与常见方案脚本都不读取 {field.KeyPath}（字段 {field.Id}）——写进补丁也不会生效。");
        }
        // 曾以不存在的 key_binder/*_pair 键固化过三个 placebo 控件，防止回归。
        Assert.DoesNotContain(schema.Sections.SelectMany(s => s.Fields),
            f => f.KeyPath is "key_binder/select_pair" or "key_binder/cursor_pair" or "key_binder/page_pair");
        Assert.All(schema.Sections.Single(s => s.Id == "keyBinder").Fields,
            f => Assert.Equal("hotkey", f.Type)); // get_string 标量；列表值脚本读不出来
    }

    [Fact]
    public void Squirrel_schema_targets_squirrel_yaml_with_dynamic_color_schemes()
    {
        var schema = SettingsSchemas.LoadForUiConfig("squirrel.yaml");

        Assert.Equal("squirrel.yaml", schema.TargetFile);
        var light = Field(schema, "skinChoice", "colorScheme");
        Assert.Equal("preset_color_schemes", light.ChoicesRef);
        Assert.Null(light.Choices);

        // Per-app behavior is part of the looks schema (bundle IDs).
        Assert.Contains(schema.Sections, s => s.Id == "appOptions");
        // 逐应用可编辑选项按前端分流：鼠须管认 inline/no_inline（Squirrel 源码里
        // 经 set_option 转发、InputController 消费），没有 inline_preedit。
        var apps = schema.Sections.Single(s => s.Id == "appOptions").Fields.Single(f => f.Type == "appOptions");
        Assert.Equal(["ascii_mode", "vim_mode", "inline", "no_inline"], apps.Choices);
        // 外观目标按页面分组：皮肤库 / 候选窗排版 / 应用行为，一个不多一个不少。
        Assert.Equal(["apps", "layout", "skin"],
            schema.Sections.Select(s => s.Group).Distinct().Order().ToList());
        Assert.All(schema.Sections, s => Assert.False(string.IsNullOrWhiteSpace(s.Group)));
    }

    [Fact]
    public void Squirrel_schema_key_paths_are_all_read_by_squirrel_theme()
    {
        // 键集对照 rime/squirrel master 的 SquirrelTheme.swift（config.get*
        // 实际读取的每个键）与自带默认 squirrel.yaml 逐一核实。小狼毫
        // layout/* 一层的键鼠须管一概不读——抄进来就是有去无回的 placebo。
        var schema = SettingsSchemas.LoadForUiConfig("squirrel.yaml");

        var squirrelReadKeys = new HashSet<string>
        {
            "style/color_scheme", "style/color_scheme_dark",
            "style/candidate_list_layout", "style/text_orientation",
            "style/candidate_format", "style/inline_preedit", "style/inline_candidate",
            "style/translucency", "style/mutual_exclusive", "style/memorize_size",
            "style/show_paging", "style/alpha",
            "style/corner_radius", "style/hilited_corner_radius",
            "style/surrounding_extra_expansion",
            "style/border_height", "style/border_width",
            "style/line_spacing", "style/spacing", "style/base_offset", "style/shadow_size",
            "style/font_face", "style/font_point",
            "style/label_font_face", "style/label_font_point",
            "style/comment_font_face", "style/comment_font_point",
            "style/preedit_font_face", "style/preedit_font_point",
            "keyboard_layout", "chord_duration", "show_notifications_when", "status_icon/show",
            "style/status_message_type", // SquirrelTheme.swift 也读；schema 暂未暴露，留给未来字段
        };
        foreach (var field in schema.Sections.SelectMany(s => s.Fields))
        {
            if (field.Type == "appOptions") continue; // app_options is read by every frontend
            Assert.True(
                squirrelReadKeys.Contains(field.KeyPath),
                $"鼠须管不读取 {field.KeyPath}（字段 {field.Id}）——别把小狼毫的键抄进 squirrel schema。");
        }
        // 小狼毫专属键（margin_*/hilite_* 等）曾混进 schema，控件调了没有任何效果。
        Assert.DoesNotContain(schema.Sections.SelectMany(s => s.Fields),
            f => f.KeyPath.StartsWith("style/layout/", StringComparison.Ordinal)
                 || f.KeyPath is "style/margin_x" or "style/margin_y" or "style/candidate_spacing"
                     or "style/hilite_spacing" or "style/hilite_padding");
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
            "style/inline_preedit", "style/preedit_type",
            "style/display_tray_icon",
            "style/paging_on_scroll", "style/font_face", "style/font_point",
            "style/label_font_face", "style/label_font_point", "style/label_format",
            "style/mark_text", "style/comment_font_face", "style/comment_font_point",
            "style/hover_type", "style/candidate_abbreviate_length",
            "style/antialias_mode", "style/ascii_tip_follow_cursor", "style/fullscreen",
            "style/vertical_text_left_to_right", "style/vertical_text_with_wrap",
            "style/vertical_auto_reverse",
            "style/layout/corner_radius", "style/layout/hilited_corner_radius",
            "style/layout/linespacing", "style/layout/spacing",
            "style/layout/candidate_spacing", "style/layout/hilite_spacing",
            "style/layout/hilite_padding", "style/layout/margin_x", "style/layout/margin_y",
            "style/layout/border_width", "style/layout/shadow_radius",
            "style/layout/shadow_offset_x", "style/layout/shadow_offset_y",
            "style/layout/min_width", "style/layout/min_height",
            "style/layout/max_width", "style/layout/max_height",
            "style/layout/align_type", "style/layout/baseline",
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
        // 逐应用可编辑选项按前端分流：小狼毫经 set_option 消费 inline_preedit
        // （RimeWithWeasel._LoadAppOptions），没有鼠须管的 inline/no_inline。
        var apps = schema.Sections.Single(s => s.Id == "appOptions").Fields.Single(f => f.Type == "appOptions");
        Assert.Equal(["ascii_mode", "vim_mode", "inline_preedit"], apps.Choices);
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
