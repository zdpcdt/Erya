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
    public async Task Appearance_page_renders_both_previews_in_a_fixed_band()
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

        // 亮/暗两个预览窗并排（定位到两个预览宿主，而非按文本数——皮肤行的
        // 迷你预览也带 "ni hao"）。
        var previewHosts = page.GetVisualDescendants().OfType<ContentControl>()
            .Where(cc => cc.Content is CandidatePreviewViewModel).ToList();
        Assert.Equal(2, previewHosts.Count);
        foreach (var h in previewHosts)
        {
            // 每个预览窗内：编码行 + 三条候选。
            var hostTexts = h.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("ni hao", hostTexts);
            Assert.Equal(["你好", "拟好", "尼奥"],
                hostTexts.Where(t => t is "你好" or "拟好" or "尼奥"));
        }
        var texts = page.GetVisualDescendants().OfType<TextBlock>()
            .Select(t => t.Text).ToList();
        Assert.Contains("亮色皮肤", texts);
        Assert.Contains("暗色皮肤", texts);

        // 预览带固定在设置区之外：预览宿主不在任何 ScrollViewer 里，
        // 滚动设置不会把预览带走。
        Assert.All(previewHosts, h =>
            Assert.DoesNotContain(h.GetVisualAncestors(), a => a is ScrollViewer));

        // 没有暗色皮肤选择时，暗色实例回落亮色（Caption 如实显示皮肤名）。
        Assert.Contains("皮肤：macos_light", texts);

        Capture(window, Path.Combine(Path.GetTempPath(), "erya-preview-light.png"), ThemeVariant.Light);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Appearance_scheme_rows_are_readonly_and_user_schemes_edit_via_dialog()
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

        // 行是只读摘要：没有 Detail 展开区（旧内联表单已移除）。
        Assert.Null(Row("macos_light").Detail);
        // 内置皮肤：编辑按钮在树上但隐藏；自定义皮肤：可点。
        Ursa.Controls.IconButton EditButton(Cell row) => row.GetVisualDescendants().OfType<Ursa.Controls.IconButton>()
            .Single(b => (ToolTip.GetTip(b) as string) == "编辑此皮肤（对话框）");
        Assert.False(IsEffectivelyVisible(EditButton(Row("macos_light"))));
        var edit = EditButton(Row("user_dark"));
        Assert.True(IsEffectivelyVisible(edit));
        var schemeVm = Assert.IsType<ColorSchemeViewModel>(edit.CommandParameter);
        Assert.Same(schemeVm, vm.SchemeLibrary!.Schemes.Single(s => s.Name == "user_dark"));

        // 编辑对话框本体：Cell 表单 + 取色器 + 固定渲染本皮肤的候选窗预览。
        var dialogVm = new SchemeEditDialogViewModel(schemeVm,
            vm.Sections.SelectMany(s => s.Fields).ToList());
        var dialog = new SchemeEditDialog { DataContext = dialogVm, Width = 600, Height = 700 };
        var host = new Window { Width = 640, Height = 760, Content = dialog };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.True(dialog.GetVisualDescendants().OfType<ColorPicker>().Count() >= 5);
        Assert.Contains(dialog.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "ni hao");
        host.Close();

        // 以此新增：内置皮肤派生成自定义皮肤（唯一名、可编辑、库变脏）。
        vm.SchemeLibrary.DuplicateSchemeCommand.Execute(vm.SchemeLibrary.Schemes.Single(s => s.Name == "macos_light"));
        Assert.True(vm.IsDirty);
        var copy = vm.SchemeLibrary.Schemes.Single(s => s.Name == "macos_light1");
        Assert.False(copy.IsShipped);

        Capture(window, Path.Combine(Path.GetTempPath(), "erya-preview-light.png"), ThemeVariant.Light);
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
