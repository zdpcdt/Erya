using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya.Controls;
using Erya.Core.Distributions;
using Erya.ViewModels.Settings;
using Erya.Views;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 设置页（Cell 版）的端到端渲染：schema 字段渲染成分组卡片里的 Cell 行、
/// 补丁徽标出现、Detail 行可展开，皮肤库渲染为预览行 + 点开编辑。
/// </summary>
public sealed class SettingsPageRenderTests : IDisposable
{
    private readonly string _dir;

    public SettingsPageRenderTests()
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

    private static (SettingsEditorViewModel Vm, SettingsPage Page, Window Window) Show(SettingsEditorViewModel vm)
    {
        var page = new SettingsPage { DataContext = vm, Width = 780, Height = 780 };
        var window = new Window { Width = 820, Height = 820, Content = page };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, page, window);
    }

    private static void Capture(Window window, string path, ThemeVariant variant)
    {
        Application.Current!.RequestedThemeVariant = variant;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(path);
        Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        Dispatcher.UIThread.RunJobs();
    }

    private static bool IsEffectivelyVisible(Control control)
        => control.IsVisible
           && control.GetVisualAncestors().OfType<Control>().All(a => a.IsVisible);

    [AvaloniaFact]
    public async Task Input_settings_render_as_cell_groups_and_rows()
    {
        Write("default.yaml", """
            menu:
              page_size: 5
            ascii_composer:
              good_old_caps_lock: true
              switch_key:
                Caps_Lock: commit_code
            """);
        Write("default.custom.yaml", "patch:\n  menu/page_size: 9\n");

        var vm = new SettingsEditorViewModel(new StubDistribution(_dir), "default");
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsSupported);

        var (_, page, window) = Show(vm);

        // 每个 schema 分节一张卡片；字段渲染成 Cell 行。
        // （皮肤库容器在 default 目标下由父 StackPanel 折叠，需按“有效可见”过滤。）
        var groups = page.GetVisualDescendants().OfType<CellGroup>()
            .Where(IsEffectivelyVisible).ToList();
        Assert.Equal(vm.Sections.Count, groups.Count);
        var cells = page.GetVisualDescendants().OfType<Cell>().ToList();
        Assert.Equal(vm.Sections.Sum(s => s.Fields.Count), cells.Count);
        Assert.True(cells.Count >= 10);

        // Detail 行：展开后详情可见，行内点击收起。
        var detailCell = cells.First(c => c.Detail is not null);
        detailCell.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(detailCell.GetVisualDescendants()
            .OfType<Border>().Single(b => b.Name == "PART_DetailHost").IsVisible);

        // 快捷键行：视觉顺序与字段顺序一致，展开后用 KeyGestureInput 录入。
        var fields = vm.Sections.SelectMany(s => s.Fields).ToList();
        var hotkeyCell = cells[fields.FindIndex(f => f is ListFieldViewModel)];
        hotkeyCell.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(hotkeyCell.GetVisualDescendants(), d => d is Ursa.Controls.KeyGestureInput);

        // 记忆选项行也展开，截屏才能拍到编辑区。
        cells[fields.FindIndex(f => f is CheckListFieldViewModel)].IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        // 键位映射：左列按键名按快捷键输入框同款风格显示（Caps_Lock → CapsLock），
        // 不再出现 RIME 原始 token。
        var mappingCell = cells[fields.FindIndex(f => f is KeyMappingFieldViewModel)];
        mappingCell.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        var keyTexts = mappingCell.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!).ToList();
        Assert.Contains("CapsLock", keyTexts);
        Assert.DoesNotContain("Caps_Lock", keyTexts);

        // 滚到键位映射行：它在分节里最靠下，滚到它，上方的快捷键/记忆选项也在视野内。
        mappingCell.BringIntoView();
        Dispatcher.UIThread.RunJobs();

        Capture(window, Path.Combine(Path.GetTempPath(), "erya-settings-input-light.png"), ThemeVariant.Light);
        Capture(window, Path.Combine(Path.GetTempPath(), "erya-settings-input-dark.png"), ThemeVariant.Dark);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Appearance_page_renders_candidate_window_preview()
    {
        Write("squirrel.yaml", """
            style:
              color_scheme: macos_light
              font_point: 18
              corner_radius: 8
            preset_color_schemes:
              macos_light:
                author: ssnhd
                back_color: 0xECECEC
                candidate_text_color: 0x000000
                hilited_candidate_back_color: 0xD3E3F6
                hilited_candidate_text_color: 0x000000
                comment_text_color: 0x888888
            """);

        var vm = new SettingsEditorViewModel(new StubDistribution(_dir), "ui");
        await vm.LoadCommand.ExecuteAsync(null);
        var (_, page, window) = Show(vm);

        // 预览卡片在页首：编码行 + 三条候选（皮肤卡片的迷你预览是
        // "1. 你好" 整串文本，不会撞上这里的裸候选词）。
        var texts = page.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).ToList();
        Assert.Contains("ni hao", texts);
        Assert.Equal(3, texts.Count(t => t is "你好" or "拟好" or "尼奥"));
        Assert.Contains("皮肤：macos_light", texts);
        // 暗色开关存在且可切换：没有暗色皮肤时回落到亮色，背景不变。
        var dark = page.GetVisualDescendants().OfType<ToggleSwitch>()
            .Single(t => t.DataContext is CandidatePreviewViewModel);
        var backBefore = Assert.IsAssignableFrom<ISolidColorBrush>(previewBack(page)).Color;
        dark.IsChecked = true;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(backBefore, Assert.IsAssignableFrom<ISolidColorBrush>(previewBack(page)).Color);

        Capture(window, Path.Combine(Path.GetTempPath(), "erya-preview-light.png"), ThemeVariant.Light);
        window.Close();
    }

    private static IBrush? previewBack(SettingsPage page)
    {
        Border? win = null;
        foreach (var b in page.GetVisualDescendants().OfType<Border>())
            if (b.Background is ISolidColorBrush { Color: var c } && c is { R: 0xEC, G: 0xEC, B: 0xEC })
            { win = b; break; }
        return win?.Background;
    }

    [AvaloniaFact]
    public async Task Appearance_settings_render_scheme_library_as_rows()
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

        var vm = new SettingsEditorViewModel(new StubDistribution(_dir), "ui");
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.HasSchemeLibrary);

        var (_, page, window) = Show(vm);

        // 每套皮肤一行：名称 + 候选条预览，点开是编辑表单。
        var schemeCells = page.GetVisualDescendants().OfType<Cell>()
            .Where(c => (c.Content as string) == "macos_light")
            .ToList();
        var schemeRow = Assert.Single(schemeCells);
        Assert.NotNull(schemeRow.Detail);
        schemeRow.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        var detail = schemeRow.GetVisualDescendants()
            .OfType<Border>().Single(b => b.Name == "PART_DetailHost");
        Assert.True(detail.IsVisible);

        // 颜色编辑用 ColorPicker（背景/候选文字/高亮背景/高亮文字/注释 ≥ 5 个）。
        Assert.True(schemeRow.GetVisualDescendants().OfType<ColorPicker>().Count() >= 5);
        // 字体编辑是固定宽度的系统字体下拉（不可编辑）——合并默认值可能是
        // ~200 字符的 Emoji 回退长串，不能撑开挤掉同行控件。
        var fontCell = Assert.Single(
            page.GetVisualDescendants().OfType<Cell>().Where(c => (c.Content as string) == "字体"));
        var fontCombo = Assert.Single(fontCell.GetVisualDescendants().OfType<ComboBox>());
        Assert.False(fontCombo.IsEditable);
        Assert.Equal(240, fontCombo.Width);
        // 皮肤库里的字体同样固定宽度，空值显示「未设置」。
        var schemeFontCombo = schemeRow.GetVisualDescendants().OfType<ComboBox>().Single();
        Assert.Equal(150, schemeFontCombo.Width);
        Assert.Equal("未设置", schemeFontCombo.SelectedItem);

        // 皮肤库在页面底部：滚到底再截屏，截图才能拍到展开的编辑表单。
        page.GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToEnd();
        Dispatcher.UIThread.RunJobs();
        Capture(window, Path.Combine(Path.GetTempPath(), "erya-settings-appearance-light.png"), ThemeVariant.Light);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Scheme_delete_button_is_wired_to_its_row()
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
        var vm = new SettingsEditorViewModel(new StubDistribution(_dir), "ui");
        await vm.LoadCommand.ExecuteAsync(null);
        var (_, page, window) = Show(vm);

        Cell Row(string name) => page.GetVisualDescendants().OfType<Cell>()
            .Single(c => (c.Content as string) == name);

        // The user scheme row's delete button must carry the row as its
        // CommandParameter — without it the command gets null and silently
        // does nothing (the「删除皮肤没有反应」regression).
        var delete = Row("user_dark").GetVisualDescendants()
            .OfType<Ursa.Controls.IconButton>().Single(b => b.Classes.Contains("Danger"));
        var rowVm = Assert.IsType<ColorSchemeViewModel>(delete.CommandParameter);
        delete.Command!.Execute(delete.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(vm.SchemeLibrary!.Schemes, s => s.Name == rowVm.Name);

        // Shipped schemes have no delete button visible at all.
        var shippedDelete = Row("shipped_light").GetVisualDescendants()
            .OfType<Ursa.Controls.IconButton>().Single(b => b.Classes.Contains("Danger"));
        Assert.False(shippedDelete.IsVisible);

        window.Close();
    }
}
