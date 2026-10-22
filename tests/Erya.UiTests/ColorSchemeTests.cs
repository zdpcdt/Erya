using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Erya.Core.Configuration;
using Erya.Core.Distributions;
using Erya.ViewModels.Settings;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 皮肤库：schemes load as shipped (base file) + patched (per-scheme and
/// per-field flat patch keys). Saving edits a shipped scheme through
/// per-field keys (preset_color_schemes/&lt;id&gt;/&lt;field&gt;) so the fields the
/// editor does not model survive, writes user-created schemes whole as
/// preset_color_schemes/&lt;id&gt; — never the whole collection, which would
/// REPLACE the shipped schemes — and previews swap RIME's BGR byte order into
/// RGB brushes.
/// </summary>
public sealed class ColorSchemeTests : IDisposable
{
    private readonly string _dir;

    public ColorSchemeTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "erya-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private void Write(string fileName, string content)
        => File.WriteAllText(Path.Combine(_dir, fileName), content);

    private SettingsEditorViewModel LoadUi()
    {
        var vm = new SettingsEditorViewModel(new StubDistribution(_dir), "ui");
        vm.LoadCommand.Execute(null);
        return vm;
    }

    [AvaloniaFact]
    public async Task Saving_writes_field_keys_for_shipped_schemes_and_whole_keys_for_new_ones()
    {
        Write("squirrel.yaml", """
            preset_color_schemes:
              macos_light:
                author: ssnhd
                back_color: 0xECECEC
                candidate_text_color: 0x000000
                hilited_candidate_back_color: 0xD3E3F6
                hilited_candidate_text_color: 0x000000
                label_color: 0x888888
              macos_dark:
                back_color: 0x1C1C1E
            style:
              color_scheme: macos_light
            """);
        var vm = LoadUi();

        Assert.True(vm.HasSchemeLibrary);
        var library = vm.SchemeLibrary!;
        var light = Assert.Single(library.Schemes, s => s.Name == "macos_light");
        var dark = Assert.Single(library.Schemes, s => s.Name == "macos_dark");
        Assert.True(light.IsShipped);
        Assert.False(light.CanRemove); // shipped schemes live in the base file
        Assert.False(library.IsModified);
        Assert.False(vm.IsDirty);

        // Edit a color and add a scheme derived from the first one.
        light.BackColor = "1C1C1E";
        // Font combo: a null write-back (ComboBox items reset) means 未设置,
        // then a normal selection writes font_face like any scheme key.
        light.SelectedFontFace = null!;
        Assert.Equal("未设置", light.SelectedFontFace);
        light.SelectedFontFace = "PingFang SC";
        library.DuplicateSchemeCommand.Execute(light);
        Assert.True(vm.IsDirty);

        await vm.SaveAndDeployCommand.ExecuteAsync(null);

        var store = new RimeConfigStore(_dir);
        var patch = store.ReadPatchValues("squirrel.yaml");
        Assert.DoesNotContain("preset_color_schemes", patch.Keys);
        // The shipped scheme is edited through per-field keys — just the two
        // fields that changed — never a whole-scheme key, which would drop
        // every field this editor does not model.
        Assert.Equal("0x1C1C1E", patch["preset_color_schemes/macos_light/back_color"]);
        Assert.Equal("PingFang SC", patch["preset_color_schemes/macos_light/font_face"]);
        Assert.Equal(2, patch.Keys.Count(k => k.StartsWith("preset_color_schemes/macos_light/")));
        Assert.DoesNotContain("preset_color_schemes/macos_light", patch.Keys);
        // The added scheme has no shipped definition to merge into: whole key.
        Assert.Contains("preset_color_schemes/macos_light1", patch.Keys); // seeded from the first scheme
        Assert.DoesNotContain("preset_color_schemes/macos_dark", patch.Keys);
        Assert.False(vm.IsDirty);

        // The merged view still carries every scheme, and the shipped fields
        // the editor never touched are still there next to the edit.
        Assert.Equal(
            ["macos_dark", "macos_light", "macos_light1"],
            store.ListMappingKeys("squirrel.yaml", "preset_color_schemes").Order().ToList());
        Assert.Equal("ssnhd", store.ReadMergedValue("squirrel.yaml", "preset_color_schemes/macos_light/author"));
        Assert.Equal("0x888888", store.ReadMergedValue("squirrel.yaml", "preset_color_schemes/macos_light/label_color"));
        Assert.Equal("0x1C1C1E", store.ReadMergedValue("squirrel.yaml", "preset_color_schemes/macos_light/back_color"));

        var patched = File.ReadAllText(Path.Combine(_dir, "squirrel.custom.yaml"));
        Assert.Contains("preset_color_schemes/macos_light/back_color: 0x1C1C1E", patched);
    }

    [AvaloniaFact]
    public async Task Whole_scheme_key_on_a_shipped_scheme_becomes_field_keys_and_dropped_fields_return()
    {
        // 琥珀色熒光屏-style scheme: candidates take text_color, there is no
        // candidate_text_color of its own.
        Write("squirrel.yaml", """
            preset_color_schemes:
              retro:
                name: 琥珀色
                text_color: 0x0093FF
                back_color: 0x000000
                label_color: 0x0093FF
                hilited_candidate_text_color: 0x000000
                hilited_candidate_back_color: 0x0093FF
                corner_radius: 16
            """);
        // What older builds wrote after any edit: the editor's fixed field set
        // with its fallbacks filled in (black candidate text on the black
        // back), every field it did not model gone.
        Write("squirrel.custom.yaml", """
            patch:
              preset_color_schemes/retro:
                back_color: 0x000000
                candidate_text_color: 0x000000
                hilited_candidate_back_color: 0x0093FF
                hilited_candidate_text_color: 0x000000
                corner_radius: 16
            """);
        var vm = LoadUi();
        var retro = Assert.Single(vm.SchemeLibrary!.Schemes, s => s.Name == "retro");
        Assert.True(retro.IsShipped);
        Assert.True(retro.IsPatched);

        retro.BackColor = "101010";
        await vm.SaveAndDeployCommand.ExecuteAsync(null);

        var store = new RimeConfigStore(_dir);
        var patch = store.ReadPatchValues("squirrel.yaml");
        Assert.DoesNotContain("preset_color_schemes/retro", patch.Keys); // whole key converted…
        Assert.Equal("0x101010", patch["preset_color_schemes/retro/back_color"]); // …into the one real edit
        Assert.Single(patch.Keys, k => k.StartsWith("preset_color_schemes/retro/"));

        // Fields the whole key had dropped are back from the shipped
        // definition, and the fallback it injected is gone.
        Assert.Equal("0x0093FF", store.ReadMergedValue("squirrel.yaml", "preset_color_schemes/retro/text_color"));
        Assert.Equal("0x0093FF", store.ReadMergedValue("squirrel.yaml", "preset_color_schemes/retro/label_color"));
        Assert.Equal("琥珀色", store.ReadMergedValue("squirrel.yaml", "preset_color_schemes/retro/name"));
        Assert.Null(store.ReadMergedValue("squirrel.yaml", "preset_color_schemes/retro/candidate_text_color"));
    }

    [AvaloniaFact]
    public async Task Setting_a_shipped_field_back_to_its_shipped_value_removes_the_field_key()
    {
        Write("squirrel.yaml", """
            preset_color_schemes:
              macos_light:
                back_color: 0xECECEC
            """);
        Write("squirrel.custom.yaml", """
            patch:
              preset_color_schemes/macos_light/back_color: 0x1C1C1E
              preset_color_schemes/macos_light/font_face: PingFang SC
            """);
        var vm = LoadUi();
        var light = Assert.Single(vm.SchemeLibrary!.Schemes, s => s.Name == "macos_light");
        Assert.True(light.IsPatched);
        Assert.Equal("1C1C1E", light.BackColor); // the field key is what the editor shows

        light.BackColor = "ECECEC";
        await vm.SaveAndDeployCommand.ExecuteAsync(null);

        var patch = new RimeConfigStore(_dir).ReadPatchValues("squirrel.yaml");
        Assert.DoesNotContain("preset_color_schemes/macos_light/back_color", patch.Keys);
        Assert.Equal("PingFang SC", patch["preset_color_schemes/macos_light/font_face"]); // the untouched key stays
    }

    [AvaloniaFact]
    public async Task Legacy_whole_collection_patch_migrates_to_per_scheme_keys()
    {
        // The shape older builds wrote: one collection key that REPLACED all
        // shipped schemes in the deployed config.
        Write("squirrel.yaml", """
            preset_color_schemes:
              shipped_light:
                back_color: 0xECECEC
            """);
        Write("squirrel.custom.yaml", """
            patch:
              preset_color_schemes:
                user_dark:
                  back_color: 0x1C1C1E
            """);
        var vm = LoadUi();

        var library = vm.SchemeLibrary!;
        // Shipped and patched schemes both show up.
        Assert.Contains(library.Schemes, s => s.Name == "shipped_light" && s.IsShipped);
        var userDark = Assert.Single(library.Schemes, s => s.Name == "user_dark");
        Assert.False(userDark.IsShipped);
        Assert.True(userDark.CanRemove);
        Assert.True(library.IsModified);

        userDark.BackColor = "0F0F10";
        await vm.SaveAndDeployCommand.ExecuteAsync(null);

        var patch = new RimeConfigStore(_dir).ReadPatchValues("squirrel.yaml");
        Assert.DoesNotContain("preset_color_schemes", patch.Keys); // legacy key migrated away
        var migrated = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            patch["preset_color_schemes/user_dark"]);
        Assert.Equal("0x0F0F10", migrated["back_color"]);

        // …and the shipped scheme is back in the effective config.
        Assert.Contains(
            "shipped_light",
            new RimeConfigStore(_dir).ListMappingKeys("squirrel.yaml", "preset_color_schemes"));
    }

    [AvaloniaFact]
    public async Task Removing_a_user_scheme_removes_its_flat_key()
    {
        Write("squirrel.yaml", """
            preset_color_schemes:
              shipped_light:
                back_color: 0xECECEC
            """);
        Write("squirrel.custom.yaml", """
            patch:
              "preset_color_schemes/user_dark":
                back_color: 0x1C1C1E
            """);
        var vm = LoadUi();

        var library = vm.SchemeLibrary!;
        var userDark = Assert.Single(library.Schemes, s => s.Name == "user_dark");
        library.RemoveSchemeCommand.Execute(userDark);
        Assert.True(vm.IsDirty);

        await vm.SaveAndDeployCommand.ExecuteAsync(null);

        var store = new RimeConfigStore(_dir);
        Assert.DoesNotContain("preset_color_schemes/user_dark", store.ReadPatchValues("squirrel.yaml").Keys);
        Assert.Contains("shipped_light", store.ListMappingKeys("squirrel.yaml", "preset_color_schemes"));
    }

    [AvaloniaFact]
    public void Previews_swap_bgr_byte_order_to_rgb()
    {
        Write("squirrel.yaml", """
            preset_color_schemes:
              blue:
                back_color: 0xFF0000
            """);
        var vm = LoadUi();
        var blue = Assert.Single(vm.SchemeLibrary!.Schemes, s => s.Name == "blue");

        // 0xFF0000 in BGR is pure blue when displayed as RGB.
        var brush = Assert.IsType<SolidColorBrush>(blue.BackPreview);
        Assert.Equal(byte.MinValue, brush.Color.R);
        Assert.Equal(byte.MinValue, brush.Color.G);
        Assert.Equal(byte.MaxValue, brush.Color.B);
    }

    [AvaloniaFact]
    public void Copy_yaml_builds_a_quoted_patch_snippet()
    {
        Write("squirrel.yaml", """
            preset_color_schemes:
              macos_light:
                name: 浅色
                author: ssnhd
                back_color: 0xECECEC
                candidate_text_color: 0x000000
                hilited_candidate_back_color: 0xD3E3F6
                hilited_candidate_text_color: 0x000000
                label_color: 0x888888
            style:
              color_scheme: macos_light
            """);
        var vm = LoadUi();
        var light = vm.SchemeLibrary!.Schemes.Single(s => s.Name == "macos_light");

        var yaml = light.BuildYamlSnippet();
        Assert.StartsWith("preset_color_schemes:\n  \"macos_light\":\n", yaml);
        // 0x 颜色必须带引号：YAML 1.1 会把裸 0xECECEC 读成整数。
        Assert.Contains("    back_color: \"0xECECEC\"", yaml);
        Assert.Contains("    name: \"浅色\"", yaml);
        // 原作者留在皮肤定义里（卡片展示），复制片段不带。
        Assert.DoesNotContain("author", yaml);
        Assert.Contains("    label_color: \"0x888888\"", yaml); // 未建模键原样带上
        // 无头环境没有主窗口剪贴板——命令不应抛错（静默跳过复制，只出提示）。
        light.CopyYamlCommand.Execute(null);
    }

    private sealed class StubDistribution : RimeDistributionBase
    {
        public StubDistribution(string userDir) => UserDir = userDir;

        public override string Id => "stub";

        public override string DisplayName => "测试发行版";

        public override string UserDir { get; }

        public override string? UiConfigFile => "squirrel.yaml";

        public override bool CanDeploy => false;

        public override Task DeployAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
