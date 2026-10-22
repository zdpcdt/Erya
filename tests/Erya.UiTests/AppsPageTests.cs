using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya.Controls;
using Erya.Core.Distributions;
using Erya.ViewModels.Settings;
using Erya.Views;
using Xunit;
using Banner = Ursa.Controls.Banner;

namespace Erya.UiTests;

/// <summary>
/// 应用行为页（专用卡片列表）与添加应用对话框：数据走共享编辑器的
/// app_options 字段（选项集由 schema choices 按前端分流），页面自己管
/// 卡片/搜索/空态，对话框管预设与校验。
/// </summary>
public sealed class AppsPageTests : IDisposable
{
    private readonly string _dir;

    public AppsPageTests()
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

    private sealed class StubUiDistribution : RimeDistributionBase
    {
        private readonly string _uiConfigFile;

        public StubUiDistribution(string userDir, string uiConfigFile)
        {
            UserDir = userDir;
            _uiConfigFile = uiConfigFile;
        }

        public override string Id => "stub";
        public override string DisplayName => "测试发行版";
        public override string UserDir { get; }
        public override string? UiConfigFile => _uiConfigFile;
        public override bool CanDeploy => false;
        public override Task DeployAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private static async Task<(AppsPageViewModel Vm, AppsPage Page, Window Window)> ShowAsync(
        RimeDistributionBase distribution)
    {
        var editor = new SettingsEditorViewModel(distribution, "ui");
        await editor.LoadCommand.ExecuteAsync(null);
        var vm = new AppsPageViewModel(editor);
        var page = new AppsPage { DataContext = vm, Width = 760, Height = 620 };
        var window = new Window { Width = 800, Height = 680, Content = page };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, page, window);
    }

    [AvaloniaFact]
    public async Task Apps_page_renders_cards_search_and_states()
    {
        Write("squirrel.yaml", """
            style:
              color_scheme: macos_light
            app_options:
              com.apple.Terminal:
                ascii_mode: true
              com.googlecode.iterm2:
                vim_mode: true
            """);
        var (vm, page, window) = await ShowAsync(new StubUiDistribution(_dir, "squirrel.yaml"));

        // 每应用一张卡片：摘要随勾选联动（Terminal 默认英文、iTerm2 Vim 模式）。
        var cells = page.GetVisualDescendants().OfType<Cell>()
            .Where(c => c.DataContext is AppOptionsRowViewModel).ToList();
        Assert.Equal(2, cells.Count);
        Assert.True(vm.HasAnyApp);
        Assert.False(vm.SearchMiss);
        Assert.Equal("默认英文",
            vm.Field!.Rows.Single(r => r.AppId == "com.apple.Terminal").SummaryText);
        Assert.Equal("Vim 模式",
            vm.Field!.Rows.Single(r => r.AppId == "com.googlecode.iterm2").SummaryText);

        // 开关数 = 每卡选项数（鼠须管 choices：ascii/vim/inline/no_inline）。
        var toggles = page.GetVisualDescendants().OfType<ToggleSwitch>().ToList();
        Assert.Equal(vm.Field!.Rows.Sum(r => r.Options.Count), toggles.Count);

        // 搜索过滤：只剩匹配行；全滤掉时「没有匹配」，清空复原。
        vm.SearchText = "不存在";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(vm.FilteredRows);
        Assert.True(vm.SearchMiss);

        vm.SearchText = "";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(2, vm.FilteredRows.Count());
        window.Close();
    }

    [AvaloniaFact]
    public async Task Apps_page_empty_state_offers_quick_presets()
    {
        Write("squirrel.yaml", "style:\n  color_scheme: macos_light\n");

        // 真实组装顺序：页面 VM 先构造（此时还没有字段），编辑器加载后补挂。
        // 预设注入 Windows 列表：生产按平台取，Linux 上为空，测试须与平台无关。
        var editor = new SettingsEditorViewModel(new StubUiDistribution(_dir, "squirrel.yaml"), "ui");
        var vm = new AppsPageViewModel(editor, AppBehaviorPresets.Windows);
        Assert.Null(vm.Field);
        await editor.LoadCommand.ExecuteAsync(null);
        Assert.NotNull(vm.Field);

        var page = new AppsPage { DataContext = vm, Width = 760, Height = 620 };
        var window = new Window { Width = 800, Height = 680, Content = page };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Assert.False(vm.HasAnyApp);

        // 空态：预设快捷按钮（点一下=添加为「默认英文」），列表区没有卡片。
        var presetButtons = page.GetVisualDescendants().OfType<Button>()
            .Where(b => b.DataContext is AppPresetItem).ToList();
        Assert.Equal(vm.EmptyStatePresets.Count, presetButtons.Count);
        Assert.DoesNotContain(page.GetVisualDescendants(), c => c is Cell { DataContext: AppOptionsRowViewModel });

        vm.QuickAddCommand.Execute(vm.EmptyStatePresets[0]);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.HasAnyApp);
        var added = Assert.Single(vm.Field!.Rows);
        Assert.Equal(vm.EmptyStatePresets[0].Id, added.AppId);
        Assert.True(added.Options.Single(o => o.Key == "ascii_mode").IsChecked);
        Assert.True(vm.IsDirty);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Weasel_apps_page_options_follow_weasel_schema_choices()
    {
        // 小狼毫与鼠须管读的键不同（inline_preedit vs inline/no_inline）：
        // 卡片开关数跟着各自 schema 的 choices 走。
        Write("weasel.yaml", """
            style:
              color_scheme: aqua
            preset_color_schemes:
              aqua:
                back_color: 0xECECEC
            app_options:
              cmd.exe:
                ascii_mode: true
            """);
        var (vm, page, window) = await ShowAsync(new StubUiDistribution(_dir, "weasel.yaml"));

        var row = Assert.Single(vm.Field!.Rows);
        Assert.Equal(["ascii_mode", "vim_mode", "inline_preedit"],
            row.Options.Select(o => o.Key));
        Assert.Equal(vm.Field.Rows.Sum(r => r.Options.Count),
            page.GetVisualDescendants().OfType<ToggleSwitch>().Count());
        window.Close();
    }

    [AvaloniaFact]
    public void Add_app_dialog_validates_and_fills_from_presets()
    {
        // 预设注入 Windows 列表（生产按平台取，Linux 为空，测试须与平台无关）。
        var vm = new AddAppDialogViewModel(["cmd.exe", "gvim.exe"], AppBehaviorPresets.Windows);

        Assert.NotEmpty(vm.Presets);
        Assert.Contains(vm.Presets, p => p.Id == "cmd.exe");

        // 空标识是常态不催错；确定由 IsValid 拦。
        Assert.Null(vm.ErrorText);
        Assert.False(vm.IsValid);

        // 预设点击填入，校验通过。
        vm.PickCommand.Execute(vm.Presets.First(p => p.Id == "cmd.exe"));
        Assert.Equal("cmd.exe", vm.AppId);
        Assert.NotNull(vm.ErrorText); // 已有规则
        Assert.False(vm.IsValid);

        // 标识中间夹空白非法；首尾空白会被容忍（确定时按 trim 后的值）。
        vm.AppId = "pu tty.exe";
        Assert.NotNull(vm.ErrorText);
        vm.AppId = "putty.exe";
        Assert.Null(vm.ErrorText);
        Assert.True(vm.IsValid);
        Assert.Equal("putty.exe", vm.ConfirmId);
    }

    [AvaloniaFact]
    public void Add_app_dialog_renders_presets_running_apps_and_error_banner()
    {
        var vm = new AddAppDialogViewModel(["cmd.exe"], AppBehaviorPresets.Windows) { AppId = "cmd.exe" };
        var dialog = new AddAppDialog { DataContext = vm, Width = 500, Height = 620 };
        var window = new Window { Width = 540, Height = 680, Content = dialog };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 常用预设按钮 + 手填输入框；重复标识的红字可见。
        var presetButtons = dialog.GetVisualDescendants().OfType<Button>()
            .Where(b => b.DataContext is AppPresetItem).ToList();
        Assert.True(presetButtons.Count >= 5);
        Assert.NotNull(vm.ErrorText);
        var banner = dialog.GetVisualDescendants().OfType<Banner>().Single();
        Assert.True(banner.IsVisible);

        vm.AppId = "putty.exe";
        Dispatcher.UIThread.RunJobs();
        Assert.False(banner.IsVisible);

        // 「正在运行的应用」仅 Windows 提供（进程 exe 名才是可用的键）。
        if (OperatingSystem.IsWindows())
        {
            Assert.True(vm.ShowRunningApps);
            Assert.Contains(vm.RunningApps, id => id.EndsWith(".exe", StringComparison.OrdinalIgnoreCase));
        }
        window.Close();
    }
}
