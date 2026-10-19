using Avalonia.Headless.XUnit;
using Erya.Core.Distributions;
using Erya.Core.Distributions.Linux;
using Erya.ViewModels.Settings;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// End-to-end settings editing against a temp RIME directory: fields load
/// from the merged view, edits become .custom.yaml patch writes, and the
/// per-frontend seam (Linux has no looks YAML) degrades with a reason.
/// </summary>
public sealed class SettingsEditorTests : IDisposable
{
    private readonly string _dir;
    private readonly string _sharedDir;

    public SettingsEditorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "erya-ui-tests", Guid.NewGuid().ToString("N"));
        _sharedDir = Path.Combine(_dir, "shared");
        Directory.CreateDirectory(_sharedDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private void Write(string fileName, string content)
        => File.WriteAllText(Path.Combine(_dir, fileName), content);

    [AvaloniaFact]
    public async Task Input_settings_load_edit_and_save_as_patch()
    {
        // fcitx5-rime over a temp dir: default.yaml settings work on any frontend.
        Write("default.yaml", """
            menu:
              page_size: 5
            ascii_composer:
              good_old_caps_lock: true
              switch_key:
                Caps_Lock: commit_code
            """);
        var distribution = new Fcitx5RimeDistribution(_dir, _sharedDir, null);
        var vm = new SettingsEditorViewModel(distribution, "default");
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsSupported);
        var sections = vm.Sections.Select(s => s.Title).ToList();
        Assert.Contains("候选词菜单", sections);
        Assert.Contains("中英切换", sections);

        // Loaded from base defaults: page size 5, caps lock = commit_code.
        var pageSize = Number(vm, "menu/page_size");
        Assert.Equal(5, pageSize.Value);
        var switchKey = Rows(vm, "ascii_composer/switch_key");
        Assert.Equal("commit_code", switchKey.First(r => r.Key == "Caps_Lock").Selection);
        Assert.False(vm.IsDirty);

        // Edit: page size → 9, Shift_L → inline_ascii.
        pageSize.Value = 9;
        switchKey.First(r => r.Key == "Shift_L").SelectedOption = "inline_ascii";
        Assert.True(vm.IsDirty);

        await vm.SaveAndDeployCommand.ExecuteAsync(null);

        var custom = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));
        Assert.Contains("menu/page_size: 9", custom);
        Assert.Contains("inline_ascii", custom);
        Assert.False(vm.IsDirty); // save reloads the fields

        // The page-size field now carries the "已修改" patch badge.
        Assert.True(Number(vm, "menu/page_size").IsModified);
    }

    [AvaloniaFact]
    public async Task Unset_choice_removes_the_patch_key_on_save()
    {
        Write("default.yaml", "menu:\n  page_size: 5\n");
        Write("default.custom.yaml", "patch:\n  menu/page_size: 9\n");
        var distribution = new Fcitx5RimeDistribution(_dir, _sharedDir, null);
        var vm = new SettingsEditorViewModel(distribution, "default");
        await vm.LoadCommand.ExecuteAsync(null);

        var pageSize = Number(vm, "menu/page_size");
        Assert.True(pageSize.IsModified);
        Assert.Equal(9, pageSize.Value);

        // Revert the whole page: pending badge stays (patch exists) until undone.
        vm.RevertCommand.Execute(null);
        Assert.False(vm.IsDirty);

        // Undo the patch entry → key gone, value back to base default.
        await vm.UndoPatchKeyCommand.ExecuteAsync(pageSize);
        Assert.False(Number(vm, "menu/page_size").IsModified);
        Assert.Equal(5, Number(vm, "menu/page_size").Value);
        Assert.DoesNotContain("page_size", File.ReadAllText(Path.Combine(_dir, "default.custom.yaml")));
    }

    [AvaloniaFact]
    public async Task Linux_frontend_has_no_appearance_schema_and_says_why()
    {
        var distribution = new Fcitx5RimeDistribution(_dir, _sharedDir, null);
        var vm = new SettingsEditorViewModel(distribution, "ui");
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.False(vm.IsSupported);
        Assert.Empty(vm.Sections);
        Assert.Contains("fcitx5", vm.UnsupportedReason);
    }

    [AvaloniaFact]
    public async Task Squirrel_appearance_loads_color_scheme_choices_from_merged_config()
    {
        Write("squirrel.yaml", """
            preset_color_schemes:
              macos_light:
                author: ssnhd
                back_color: 0xFFFFFF
              macos_dark:
                back_color: 0x1C1C1E
            style:
              color_scheme: macos_light
              font_point: 22
            app_options:
              com.apple.Terminal:
                ascii_mode: true
            """);
        // A distribution stub over the temp dir — the real SquirrelDistribution
        // would point at the developer's live ~/Library/Rime.
        var distribution = new FakeSquirrelDistribution(_dir, _sharedDir);
        var vm = new SettingsEditorViewModel(distribution, "ui");
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsSupported);
        Assert.Equal("squirrel.yaml", distribution.UiConfigFile);

        var scheme = Choice(vm, "style/color_scheme");
        Assert.Equal("macos_light", scheme.Selected);
        Assert.Contains("macos_light", scheme.Options);
        Assert.Contains("macos_dark", scheme.Options);
        Assert.Equal("未设置", scheme.Options[0]); // explicit unset entry

        // app_options rows load from merged config.
        var apps = vm.Sections.SelectMany(s => s.Fields).OfType<AppOptionsFieldViewModel>().Single();
        var terminal = Assert.Single(apps.Rows, r => r.AppId == "com.apple.Terminal");
        Assert.True(terminal.AsciiMode);
    }

    /// <summary>Squirrel-like distribution over temp dirs (the real one owns ~/Library/Rime).</summary>
    private sealed class FakeSquirrelDistribution : RimeDistributionBase
    {
        public FakeSquirrelDistribution(string userDir, string sharedDir)
        {
            UserDir = userDir;
            SchemaSearchDirs = [sharedDir];
        }

        public override string Id => "squirrel";

        public override string DisplayName => "测试鼠须管";

        public override string UserDir { get; }

        public override string? UiConfigFile => "squirrel.yaml";

        public override bool CanDeploy => false;

        public override Task DeployAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }


    [AvaloniaFact]
    public async Task Weasel_layout_field_writes_highest_precedence_key()
    {
        // Mirrors a rimetool-managed dir: base carries BOTH horizontal: true
        // and layout/type: vertical. weasel reads layout/type LAST, so it is
        // the only key that actually decides the panel orientation — the UI
        // must show vertical (the merged truth) and write layout/type.
        Write("weasel.yaml", """
            style:
              horizontal: true
              vertical_text: true
              layout:
                type: vertical
              color_scheme: aqua
            preset_color_schemes:
              aqua:
                back_color: 0xECECEC
            app_options:
              cmd.exe:
                ascii_mode: true
            """);
        var vm = new SettingsEditorViewModel(new FakeWeaselDistribution(_dir, _sharedDir), "ui");
        await vm.LoadCommand.ExecuteAsync(null);

        Assert.True(vm.IsSupported);
        var layout = Choice(vm, "style/layout/type");
        Assert.Equal("vertical", layout.Selected);

        layout.Selected = "horizontal";
        Assert.True(vm.IsDirty);
        await vm.SaveAndDeployCommand.ExecuteAsync(null);

        var patch = new Erya.Core.Configuration.RimeConfigStore(_dir).ReadPatchValues("weasel.yaml");
        Assert.Equal("horizontal", patch["style/layout/type"]);
        Assert.DoesNotContain(patch.Keys, k => k is "style/horizontal" or "style/text_orientation");
        Assert.Equal("horizontal", Choice(vm, "style/layout/type").Selected); // reloaded
    }


    /// <summary>Weasel-like distribution over temp dirs (the real one owns %APPDATA%\Rime).</summary>
    private sealed class FakeWeaselDistribution : RimeDistributionBase
    {
        public FakeWeaselDistribution(string userDir, string sharedDir)
        {
            UserDir = userDir;
            SchemaSearchDirs = [sharedDir];
        }

        public override string Id => "weasel";

        public override string DisplayName => "测试小狼毫";

        public override string UserDir { get; }

        public override string? UiConfigFile => "weasel.yaml";

        public override bool CanDeploy => false;

        public override Task DeployAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static SettingsFieldViewModel Field(SettingsEditorViewModel vm, string keyPath)
        => vm.Sections.SelectMany(s => s.Fields).First(f => f.KeyPath == keyPath);

    private static NumberFieldViewModel Number(SettingsEditorViewModel vm, string keyPath)
        => Assert.IsType<NumberFieldViewModel>(Field(vm, keyPath));

    private static ChoiceFieldViewModel Choice(SettingsEditorViewModel vm, string keyPath)
        => Assert.IsType<ChoiceFieldViewModel>(Field(vm, keyPath));

    private static IReadOnlyList<KeyMappingRowViewModel> Rows(SettingsEditorViewModel vm, string keyPath)
        => Assert.IsType<KeyMappingFieldViewModel>(Field(vm, keyPath)).Rows;
}
