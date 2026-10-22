using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya.Controls;
using Erya.Core.Distributions;
using Erya.ViewModels.Settings;
using Erya.Views;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 应用行为页（专用卡片列表）与添加应用弹层：数据走共享编辑器的
/// app_options 字段（选项集由 schema choices 按前端分流），页面自己管
/// 卡片/搜索/空态，弹层管「点一下即添加」的合并过滤列表。
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
    public void Add_app_picker_filters_shows_states_and_adds_in_one_click()
    {
        // 预设注入 Windows 列表（生产按平台取，Linux 为空，测试须与平台无关）。
        var added = new List<string>();
        var vm = new AddAppPickerViewModel(["cmd.exe"], added.Add, AppBehaviorPresets.Windows);

        // 常用条目在列；已有规则的那条标「已添加」（不可点，不再事后报错）。
        var cmd = vm.PresetEntries.Single(e => e.Id == "cmd.exe");
        Assert.True(cmd.IsAdded);
        Assert.True(cmd.ShowId); // 预设条目带友好名（副行显示标识）
        Assert.Contains(vm.PresetEntries, e => e is { Id: "putty.exe", IsAdded: false });

        // 搜索按 标识/友好名 过滤（「终端」命中 Windows 终端/终端（Terminal））。
        vm.SearchText = "终端";
        Assert.Contains(vm.FilteredPresets, e => e.Title.Contains("终端"));
        Assert.DoesNotContain(vm.FilteredPresets, e => e.Id == "putty.exe");

        // 输入恰好等于某条目标识 → 点那条即可，不出「添加」行（前缀不算完全匹配）。
        vm.SearchText = "putty.exe";
        Assert.True(vm.ExactMatchExists);
        Assert.False(vm.CanAddCustom);

        // 全新标识 → 首行「添加 “xxx”」，回车/点击即加。
        vm.SearchText = "myapp.exe";
        Assert.False(vm.ExactMatchExists);
        Assert.True(vm.CanAddCustom);
        Assert.Equal("添加 “myapp.exe”", vm.CustomAddLabel);
        vm.AddCustomCommand.Execute(null);
        Assert.Equal(["myapp.exe"], added);

        // 已有规则 / 中间夹空白 → 行内提示，加不了。
        vm.SearchText = "cmd.exe";
        Assert.Equal("已有 cmd.exe 的规则。", vm.HintText);
        Assert.False(vm.CanAddCustom);
        vm.SearchText = "pu tty.exe";
        Assert.Equal("标识不能包含空白字符。", vm.HintText);
        Assert.False(vm.CanAddCustom);
        vm.AddCustomCommand.Execute(null);
        Assert.Equal(["myapp.exe"], added); // 无新增

        // 点选即加：已添加的条目点了没反应，未添加的直接回调。
        vm.PickEntryCommand.Execute(vm.PresetEntries.Single(e => e.Id == "cmd.exe"));
        Assert.Equal(["myapp.exe"], added);
        vm.PickEntryCommand.Execute(vm.PresetEntries.Single(e => e.Id == "putty.exe"));
        Assert.Equal(["myapp.exe", "putty.exe"], added);
    }

    [AvaloniaFact]
    public async Task Open_picker_adds_app_and_requests_flyout_close()
    {
        Write("squirrel.yaml", """
            style:
              color_scheme: macos_light
            app_options:
              cmd.exe:
                ascii_mode: true
            """);
        var editor = new SettingsEditorViewModel(new StubUiDistribution(_dir, "squirrel.yaml"), "ui");
        await editor.LoadCommand.ExecuteAsync(null);
        // 预设注入 Windows 列表：fixture 的 cmd.exe 要命中预设才能测「已添加」标记。
        var vm = new AppsPageViewModel(editor, AppBehaviorPresets.Windows);

        // 已有的应用在弹层里带「已添加」标记。
        vm.OpenPickerCommand.Execute(null);
        Assert.NotNull(vm.Picker);
        Assert.True(vm.Picker!.PresetEntries.Single(e => e.Id == "cmd.exe").IsAdded);
        var target = vm.Picker.PresetEntries.First(e => !e.IsAdded);

        // 点选条目 = 添加（默认英文）+ 请求视图收起弹层。
        var closeRequested = false;
        vm.RequestClosePicker += (_, _) => closeRequested = true;
        vm.Picker.PickEntryCommand.Execute(target);

        Assert.True(closeRequested);
        var addedRow = vm.Field!.Rows.Single(r => r.AppId == target.Id);
        Assert.True(addedRow.Options.Single(o => o.Key == "ascii_mode").IsChecked);
        Assert.True(vm.IsDirty);

        // 手输回车走同一条路（添加 + 关闭）。
        closeRequested = false;
        vm.OpenPickerCommand.Execute(null);
        vm.Picker!.SearchText = "org.vim.MacVim";
        vm.Picker.AddCustomCommand.Execute(null);
        Assert.True(closeRequested);
        Assert.Contains(vm.Field!.Rows, r => r.AppId == "org.vim.MacVim");
    }

    [AvaloniaFact]
    public void Add_app_flyout_renders_groups_states_and_enter_key()
    {
        // existing 取 Windows 预设第一条——测试与当前平台无关（Linux 预设为空）。
        var addedId = AppBehaviorPresets.Windows[0].Id;

        var added = new List<string>();
        var picker = new AddAppPickerViewModel([addedId], added.Add, AppBehaviorPresets.Windows);
        var flyout = new AddAppFlyout { DataContext = picker, Width = 340 };
        var window = new Window { Width = 380, Height = 560, Content = flyout };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 全量列表：预设条目渲染为按钮；已有规则的那条禁用 + 「已添加」标签。
        var entries = flyout.GetVisualDescendants().OfType<Button>()
            .Where(b => b.DataContext is AddAppEntry).ToList();
        var disabled = entries.Single(b => !b.IsEnabled);
        Assert.Equal(addedId, ((AddAppEntry)disabled.DataContext!).Id);
        // 「已添加」标签只在那条禁用条目里可见（模板里每条都带、只是折叠）。
        Assert.Contains(disabled.GetVisualDescendants().OfType<TextBlock>(),
            t => t.Text == "已添加" && t.IsVisible);

        // 点未添加的条目直接触发回调。
        var pickable = entries.First(b => b.IsEnabled);
        var pickableId = ((AddAppEntry)pickable.DataContext!).Id;
        pickable.Command.Execute(pickable.CommandParameter);
        Assert.Equal([pickableId], added);

        // 过滤：全新标识无匹配 → 预设组头隐藏，「添加 “xxx”」行出现；回车=添加。
        picker.SearchText = "myapp.exe";
        Dispatcher.UIThread.RunJobs();
        Assert.False(picker.HasFilteredPresets);
        Assert.True(picker.CanAddCustom);
        // 「添加 “xxx”」行出现（按命令身份找——TextBox 内建清除按钮同 DataContext）。
        var customRow = flyout.GetVisualDescendants().OfType<Button>()
            .Single(b => ReferenceEquals(b.Command, picker.AddCustomCommand));
        Assert.True(customRow.IsVisible);
        Assert.Equal("添加 “myapp.exe”",
            customRow.GetVisualDescendants().OfType<TextBlock>().Single().Text);

        flyout.GetVisualDescendants().OfType<TextBox>().Single().RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
        });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal([pickableId, "myapp.exe"], added);
        window.Close();
    }
}
