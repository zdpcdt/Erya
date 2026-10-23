using Erya.Core.Localization;
using Erya.Core.Settings;

namespace Erya.Core.Tests;

/// <summary>设置 schema 的语言 overlay：结构来自 base，文案跟随 CoreLoc 语言。</summary>
public sealed class SettingsSchemaOverlayTests : IDisposable
{
    private readonly string _originalLanguage = CoreLoc.Language;

    public SettingsSchemaOverlayTests()
    {
    }

    public void Dispose() => CoreLoc.Language = _originalLanguage;

    [Fact]
    public void English_overlay_changes_labels_but_keeps_structure()
    {
        CoreLoc.Language = "";
        var zh = SettingsSchemas.LoadDefault();
        CoreLoc.Language = "en";
        var en = SettingsSchemas.LoadDefault();

        // 结构（id/keyPath/默认值）不变，文案变。
        Assert.Equal(zh.Sections.Count, en.Sections.Count);
        Assert.Equal(zh.Sections[0].Fields.Count, en.Sections[0].Fields.Count);
        Assert.Equal(zh.Sections[0].Fields[0].KeyPath, en.Sections[0].Fields[0].KeyPath);

        var menu = en.Sections.Single(s => s.Id == "menu");
        Assert.Equal("Candidate Menu", menu.Title);
        Assert.Equal("Candidates per page", menu.Fields.Single(f => f.Id == "pageSize").Label);
    }

    [Fact]
    public void Base_language_keeps_simplified_chinese()
    {
        CoreLoc.Language = "";
        var zh = SettingsSchemas.LoadDefault();
        Assert.Equal("候选词菜单", zh.Sections.Single(s => s.Id == "menu").Title);
    }

    [Fact]
    public void Package_catalog_overlay_changes_description_only()
    {
        CoreLoc.Language = "en";
        var packages = Erya.Core.Packages.SchemaPackageCatalog.Load();
        var ice = packages.Single(p => p.Id == "rime-ice");
        // 名字是品牌（照抄），描述是翻译。
        Assert.Equal("雾凇拼音", ice.Name);
        Assert.StartsWith("Simplified Chinese setup", ice.Description, StringComparison.Ordinal);
    }
}
