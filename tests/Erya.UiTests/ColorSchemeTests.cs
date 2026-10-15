using System.Linq;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Erya.Core.Configuration;
using Erya.Core.Distributions;
using Erya.ViewModels.Settings;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 皮肤库：schemes load from the merged preset_color_schemes, edits rewrite
/// the whole collection as one patch key, and previews swap RIME's BGR
/// byte order into RGB brushes.
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
    public async Task Schemes_load_edit_and_save_as_one_patch_key()
    {
        Write("squirrel.yaml", """
            preset_color_schemes:
              macos_light:
                author: ssnhd
                back_color: 0xECECEC
                candidate_text_color: 0x000000
                hilited_candidate_back_color: 0xD3E3F6
                hilited_candidate_text_color: 0x000000
            style:
              color_scheme: macos_light
            """);
        var vm = LoadUi();

        Assert.True(vm.HasSchemeLibrary);
        var library = vm.SchemeLibrary!;
        var light = Assert.Single(library.Schemes, s => s.Name == "macos_light");
        Assert.Equal("ECECEC", light.BackColor);
        Assert.Equal("ssnhd", light.Author);
        Assert.False(vm.IsDirty);

        // Edit a color and add a scheme derived from the first one.
        light.BackColor = "1C1C1E";
        library.AddSchemeCommand.Execute(null);
        Assert.True(vm.IsDirty);

        await vm.SaveAndDeployCommand.ExecuteAsync(null);

        var patch = new RimeConfigStore(_dir).ReadPatchValues("squirrel.yaml");
        var schemes = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object?>>(
            patch["preset_color_schemes"]);
        Assert.Equal(2, schemes.Count);
        Assert.Contains("macos_light", schemes.Keys);
        Assert.Contains("macos_light1", schemes.Keys); // seeded from the first scheme's name
        Assert.False(vm.IsDirty);

        var patched = File.ReadAllText(Path.Combine(_dir, "squirrel.custom.yaml"));
        Assert.Contains("back_color: 0x1C1C1E", patched);
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
    public void Removing_all_schemes_removes_the_patch_key_on_save()
    {
        Write("squirrel.custom.yaml", "patch:\n  preset_color_schemes:\n    old: {}\n");
        var vm = LoadUi();

        var library = vm.SchemeLibrary!;
        Assert.True(library.IsModified); // collection carries a patch entry
        foreach (var scheme in library.Schemes.ToList())
            library.RemoveSchemeCommand.Execute(scheme);
        Assert.True(vm.IsDirty);

        vm.SaveAndDeployCommand.Execute(null);

        Assert.DoesNotContain(
            new RimeConfigStore(_dir).ReadPatchValues("squirrel.yaml"),
            kv => kv.Key == "preset_color_schemes");
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
