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
        Assert.True(terminal.Options.Single(o => o.Key == "ascii_mode").IsChecked);
    }

    [AvaloniaFact]
    public void AppOptions_save_carries_keys_beyond_the_checkboxes()
    {
        // 鼠须管逐应用选项按 schema 分流（inline/no_inline）；编辑器没有复选框的键
        // （如下面这个鼠须管不认识的 inline_preedit）保存时必须原样带回，
        // 且「由开改关」要写出显式 false 才能盖过基座默认值。
        var field = new Erya.Core.Settings.SettingsField
        {
            Id = "appsTable",
            Label = "应用列表",
            Type = "appOptions",
            KeyPath = "app_options",
            Choices = ["ascii_mode", "vim_mode", "inline", "no_inline"],
        };
        var vm = new AppOptionsFieldViewModel(field);
        vm.Load(new Dictionary<string, object?>
        {
            ["com.apple.Terminal"] = new Dictionary<string, object?>
            {
                ["ascii_mode"] = true,
                ["inline_preedit"] = true, // 鼠须管 schema 未建模 → 原样保留
            },
        });

        var row = Assert.Single(vm.Rows);
        Assert.Equal(["默认英文", "Vim 模式", "强制内嵌编码", "禁用内嵌编码"],
            row.Options.Select(o => o.Label));

        row.Options.Single(o => o.Key == "no_inline").IsChecked = true; // 开 → 写 true
        row.Options.Single(o => o.Key == "ascii_mode").IsChecked = false; // 开 → 关：显式 false
        Assert.True(vm.HasPendingEdits);

        var written = (IReadOnlyDictionary<string, object?>)vm.PendingValue()!;
        var terminal = (IReadOnlyDictionary<string, object?>)written["com.apple.Terminal"]!;
        Assert.Equal(false, terminal["ascii_mode"]);
        Assert.Equal(true, terminal["no_inline"]);
        Assert.Equal(true, terminal["inline_preedit"]); // 未建模键不丢
        Assert.False(terminal.ContainsKey("vim_mode")); // 未勾选且原本就没设 → 不写
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
    public async Task App_options_round_trip_and_add_validation_end_to_end()
    {
        // gvim 带一个 UI 不管的键（full_shape）和一个显式 false（ascii_punct）：
        // 保存必须原样带回；由开改关要写出显式 false 盖过基座默认值。
        Write("squirrel.yaml", """
            style:
              color_scheme: macos_light
            app_options:
              gvim.exe:
                ascii_mode: true
                full_shape: true
                ascii_punct: false
            """);
        var vm = new SettingsEditorViewModel(new FakeSquirrelDistribution(_dir, _sharedDir), "ui");
        await vm.LoadCommand.ExecuteAsync(null);

        var field = Assert.IsType<AppOptionsFieldViewModel>(Field(vm, "app_options"));
        var gvim = Assert.Single(field.Rows);
        Assert.True(gvim.Options.Single(o => o.Key == "ascii_mode").IsChecked);
        Assert.Equal("默认英文", gvim.SummaryText);

        // 新增 cmd.exe（默认英文）+ 添加校验（重复 / 空标识）。
        Assert.Null(field.AddApp("cmd.exe", asciiMode: true));
        Assert.NotNull(field.AddApp("  ", asciiMode: true));
        Assert.NotNull(field.AddApp("CMD.EXE", asciiMode: true)); // 重复（大小写不敏感）
        Assert.True(field.HasPendingEdits);

        var pending = Assert.IsType<Dictionary<string, object?>>(field.PendingValue());
        var cmd = Assert.IsType<Dictionary<string, object?>>(pending["cmd.exe"]);
        Assert.Equal(true, cmd["ascii_mode"]);
        Assert.DoesNotContain("vim_mode", cmd.Keys); // 未勾选且原本没设 → 不写
        var gvimOut = Assert.IsType<Dictionary<string, object?>>(pending["gvim.exe"]);
        Assert.Equal(true, gvimOut["full_shape"]); // 未建模键 round-trip 保留
        Assert.Equal(false, gvimOut["ascii_punct"]);

        // 由开改关：显式 false（盖过基座的 true），未建模键保留。
        gvim.Options.Single(o => o.Key == "ascii_mode").IsChecked = false;
        var again = Assert.IsType<Dictionary<string, object?>>(field.PendingValue());
        gvimOut = Assert.IsType<Dictionary<string, object?>>(again["gvim.exe"]!);
        Assert.Equal(false, gvimOut["ascii_mode"]);
        Assert.Equal(true, gvimOut["full_shape"]);

        // 删光所有应用 → 整段从 patch 移除（PendingRemoval）。
        foreach (var row in field.Rows.ToList()) field.RemoveRowCommand.Execute(row);
        Assert.Null(field.PendingValue());
        Assert.True(field.PendingRemoval());
    }

    [AvaloniaFact]
    public async Task App_options_add_and_save_writes_patch_then_reloads()
    {
        Write("squirrel.yaml", """
            style:
              color_scheme: macos_light
            """);
        var vm = new SettingsEditorViewModel(new FakeSquirrelDistribution(_dir, _sharedDir), "ui");
        await vm.LoadCommand.ExecuteAsync(null);

        var field = Assert.IsType<AppOptionsFieldViewModel>(Field(vm, "app_options"));
        Assert.Empty(field.Rows);
        Assert.Null(field.AddApp("com.apple.Terminal", asciiMode: true));

        await vm.SaveAndDeployCommand.ExecuteAsync(null);

        var patch = new Erya.Core.Configuration.RimeConfigStore(_dir).ReadPatchValues("squirrel.yaml");
        var apps = Assert.IsType<Dictionary<string, object?>>(patch["app_options"]);
        var terminal = Assert.IsType<Dictionary<string, object?>>(apps["com.apple.Terminal"]!);
        Assert.Equal(true, terminal["ascii_mode"]);
        // 保存后重载：行从合并值重建，选项勾选回放。
        field = Assert.IsType<AppOptionsFieldViewModel>(Field(vm, "app_options"));
        Assert.True(Assert.Single(field.Rows).Options.Single(o => o.Key == "ascii_mode").IsChecked);
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

    [AvaloniaFact]
    public async Task Weasel_font_field_loads_from_config_and_writes_patch()
    {
        // A comma fallback list is a legal weasel font_face (~200 chars on
        // some machines): it stays selectable in the combo, and 未设置 = the
        // patch key is removed.
        Write("weasel.yaml", """
            style:
              font_face: "Segoe UI Emoji:23:23, Segoe UI, Microsoft YaHei"
            """);
        var vm = new SettingsEditorViewModel(new FakeWeaselDistribution(_dir, _sharedDir), "ui");
        await vm.LoadCommand.ExecuteAsync(null);

        var font = Assert.IsType<FontFieldViewModel>(Field(vm, "style/font_face"));
        Assert.Equal("未设置", font.Options[0]);
        Assert.Contains("Segoe UI Emoji:23:23, Segoe UI, Microsoft YaHei", font.Options);
        Assert.Equal("Segoe UI Emoji:23:23, Segoe UI, Microsoft YaHei", font.SelectedOption);

        // A ComboBox whose ItemsSource resets writes null through the
        // SelectedItem binding — the wrapper must read that as 未设置, not
        // crash with a NullReferenceException (选中字体报错 regression).
        font.SelectedOption = null!;
        Assert.Equal("未设置", font.SelectedOption);

        // Selecting keeps the Options instance stable: a new list would swap
        // the ComboBox's ItemsSource, clearing the selection mid-edit.
        var options = font.Options;
        font.SelectedOption = "SimSun";
        Assert.Same(options, font.Options);

        Assert.True(vm.IsDirty);
        await vm.SaveAndDeployCommand.ExecuteAsync(null);

        var patch = new Erya.Core.Configuration.RimeConfigStore(_dir).ReadPatchValues("weasel.yaml");
        Assert.Equal("SimSun", patch["style/font_face"]);

        // 未设置 goes back to the frontend default: the patch key is removed.
        // (Save reloads the fields, so re-fetch — the old VM instance is detached.)
        Assert.IsType<FontFieldViewModel>(Field(vm, "style/font_face")).SelectedOption = "未设置";
        Assert.True(vm.IsDirty);
        await vm.SaveAndDeployCommand.ExecuteAsync(null);
        Assert.DoesNotContain("style/font_face",
            new Erya.Core.Configuration.RimeConfigStore(_dir).ReadPatchValues("weasel.yaml").Keys);
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
