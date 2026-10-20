using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Erya.Core.Configuration;
using Erya.Core.Distributions;
using Erya.ViewModels.Settings;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 皮肤库：schemes load as shipped (base file) + patched (per-scheme flat
/// patch keys), saves write only created/edited schemes as
/// preset_color_schemes/&lt;id&gt; keys — never the whole collection, which
/// would REPLACE the shipped schemes — and previews swap RIME's BGR byte
/// order into RGB brushes.
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
    public async Task Saving_writes_per_scheme_keys_and_keeps_shipped_schemes()
    {
        Write("squirrel.yaml", """
            preset_color_schemes:
              macos_light:
                author: ssnhd
                back_color: 0xECECEC
                candidate_text_color: 0x000000
                hilited_candidate_back_color: 0xD3E3F6
                hilited_candidate_text_color: 0x000000
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
        library.AddSchemeCommand.Execute(null);
        Assert.True(vm.IsDirty);

        await vm.SaveAndDeployCommand.ExecuteAsync(null);

        var patch = new RimeConfigStore(_dir).ReadPatchValues("squirrel.yaml");
        // Only the edited + added scheme — as flat per-scheme keys; the
        // untouched shipped scheme never lands in the patch.
        Assert.DoesNotContain("preset_color_schemes", patch.Keys);
        var edited = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            patch["preset_color_schemes/macos_light"]);
        Assert.Equal("0x1C1C1E", edited["back_color"]);
        Assert.Equal("PingFang SC", edited["font_face"]);
        Assert.Contains("preset_color_schemes/macos_light1", patch.Keys); // seeded from the first scheme
        Assert.DoesNotContain("preset_color_schemes/macos_dark", patch.Keys);
        Assert.False(vm.IsDirty);

        // The merged view still carries every scheme: per-scheme patch keys
        // merge INTO the shipped collection instead of replacing it.
        Assert.Equal(
            ["macos_dark", "macos_light", "macos_light1"],
            new RimeConfigStore(_dir).ListMappingKeys("squirrel.yaml", "preset_color_schemes").Order().ToList());

        var patched = File.ReadAllText(Path.Combine(_dir, "squirrel.custom.yaml"));
        Assert.Contains("back_color: 0x1C1C1E", patched);
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
