using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
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

        // 补丁徽标：menu/page_size 已在补丁里 → 行内出现“已修改”。
        var pageSize = cells.Single(c => (c.Content as string)!.Contains("候选"));
        var badge = pageSize.GetVisualDescendants().OfType<TextBlock>()
            .Single(t => t.Text == "已修改");
        Assert.True(badge.IsVisible);

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

        Capture(window, "/tmp/erya-settings-input-light.png", ThemeVariant.Light);
        Capture(window, "/tmp/erya-settings-input-dark.png", ThemeVariant.Dark);
        window.Close();
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

        Capture(window, "/tmp/erya-settings-appearance-light.png", ThemeVariant.Light);
        window.Close();
    }
}
