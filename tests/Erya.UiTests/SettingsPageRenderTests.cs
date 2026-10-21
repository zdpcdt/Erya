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
    public async Task Layout_page_renders_both_previews_and_grouped_sections()
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

        var editor = new SettingsEditorViewModel(new StubDistribution(_dir), "ui");
        await editor.LoadCommand.ExecuteAsync(null);
        var page = new LayoutPage { DataContext = new LayoutPageViewModel(editor), Width = 780, Height = 780 };
        var window = new Window { Width = 820, Height = 820, Content = page };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 亮/暗两个预览窗并排（定位到两个预览宿主，而非按文本数——皮肤列表的
        // 迷你预览也带 "ni hao"）。
        var previewHosts = page.GetVisualDescendants().OfType<ContentControl>()
            .Where(cc => cc.Content is CandidatePreviewViewModel).ToList();
        Assert.Equal(2, previewHosts.Count);
        foreach (var h in previewHosts)
        {
            var hostTexts = h.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
            Assert.Contains("ni hao", hostTexts);
            Assert.Equal(["你好", "拟好", "尼奥"],
                hostTexts.Where(t => t is "你好" or "拟好" or "尼奥"));
        }
        var texts = page.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).ToList();
        Assert.Contains("亮色皮肤", texts);
        Assert.Contains("暗色皮肤", texts);

        // 预览带固定在设置区之外：预览宿主不在任何 ScrollViewer 里。
        Assert.All(previewHosts, h =>
            Assert.DoesNotContain(h.GetVisualAncestors(), a => a is ScrollViewer));

        // 分组渲染：排版字段都在；皮肤选择/应用行为不在本页
        // （「亮色皮肤」文字同时是预览带标签，字段按 Cell 判别）。
        Assert.Contains(texts, t => t == "排列方式");
        Assert.Contains(texts, t => t == "间距");
        var cells = page.GetVisualDescendants().OfType<Cell>().Select(c => c.Content as string).ToList();
        Assert.Contains("候选字体", cells);
        Assert.DoesNotContain(cells, t => t == "亮色皮肤" || t == "应用列表");

        Capture(window, Path.Combine(Path.GetTempPath(), "erya-preview-light.png"), ThemeVariant.Light);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Skin_gallery_applies_light_scheme_and_edits_in_a_deep_page()
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
        var editor = new SettingsEditorViewModel(new StubDistribution(_dir), "ui");
        await editor.LoadCommand.ExecuteAsync(null);
        var vm = new SkinPageViewModel(editor);
        var page = new SkinPage { DataContext = vm, Width = 900, Height = 780 };
        var window = new Window { Width = 940, Height = 820, Content = page };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 画廊：两套皮肤缩略卡 + 亮/暗主题两个 Cell；没有皮肤级字体排版
        //（与「候选窗排版」页冲突，已从皮肤编辑里移除）。
        var cards = page.GetVisualDescendants().OfType<Button>()
            .Where(b => (ToolTip.GetTip(b) as string) == "应用为亮色主题").ToList();
        Assert.Equal(2, cards.Count);
        var cellTitles = page.GetVisualDescendants().OfType<Cell>().Select(c => c.Content as string).ToList();
        Assert.Contains("亮色主题", cellTitles);
        Assert.Contains("暗色主题", cellTitles);
        Assert.DoesNotContain(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "字体与排版");

        // 点卡片 = 应用为亮色主题（待保存编辑）。
        var userDark = editor.SchemeLibrary!.Schemes.Single(s => s.Name == "user_dark");
        vm.UseAsLightSchemeCommand.Execute(userDark);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("user_dark", vm.LightSchemeField!.Selected);
        Assert.True(vm.IsDirty);

        // 「编辑」进入页面内深一层：自定义皮肤可编辑，删除按钮绑到正在编辑的皮肤。
        vm.EditLightSchemeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Same(userDark, vm.EditingScheme);
        Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "← 返回皮肤库");
        Assert.Contains(page.GetVisualDescendants().OfType<TextBox>(), t => !t.IsReadOnly);
        Assert.Contains(page.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "ni hao"); // 深页预览
        var delete = page.GetVisualDescendants().OfType<Ursa.Controls.IconButton>()
            .Single(b => b.Classes.Contains("Danger"));
        Assert.Same(userDark, delete.CommandParameter);
        delete.Command!.Execute(delete.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(editor.SchemeLibrary.Schemes, s => s.Name == "user_dark");
        Assert.Null(vm.EditingScheme); // 删除后自动返回画廊，不留空深页

        // 内置皮肤深页只读。
        vm.LightSchemeField.Selected = "macos_light";
        vm.EditLightSchemeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.True(vm.EditingScheme!.IsShipped);
        Assert.Contains(page.GetVisualDescendants().OfType<TextBox>(), t => t.IsReadOnly);
        Assert.Contains(page.GetVisualDescendants().OfType<ColorPicker>(), p => !p.IsEnabled);

        // 以此新增：内置派生成自定义皮肤（唯一名、库变脏）。
        editor.SchemeLibrary.DuplicateSchemeCommand.Execute(vm.EditingScheme);
        Assert.Contains(editor.SchemeLibrary.Schemes, s => s.Name == "macos_light1");

        // 返回画廊。
        vm.BackToGalleryCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Null(vm.EditingScheme);

        Capture(window, Path.Combine(Path.GetTempPath(), "erya-preview-light.png"), ThemeVariant.Light);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Scheme_delete_button_is_wired_to_the_scheme_being_edited()
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
        var editor = new SettingsEditorViewModel(new StubDistribution(_dir), "ui");
        await editor.LoadCommand.ExecuteAsync(null);
        var vm = new SkinPageViewModel(editor);
        var page = new SkinPage { DataContext = vm, Width = 900, Height = 780 };
        var window = new Window { Width = 940, Height = 820, Content = page };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 深页里编辑自定义皮肤：删除按钮必须携带正在编辑的皮肤作 CommandParameter——
        // 否则命令收到 null 静默不动（「删除皮肤没有反应」回归）。
        var userDark = editor.SchemeLibrary!.Schemes.Single(s => s.Name == "user_dark");
        vm.LightSchemeField!.Selected = "user_dark";
        vm.EditLightSchemeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        var delete = page.GetVisualDescendants().OfType<Ursa.Controls.IconButton>()
            .Single(b => b.Classes.Contains("Danger"));
        Assert.Same(userDark, delete.CommandParameter);
        delete.Command!.Execute(delete.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(editor.SchemeLibrary.Schemes, s => s.Name == userDark.Name);
        Assert.Null(vm.EditingScheme); // 删除的是当前深页 → 返回画廊

        // 内置皮肤：编辑进入深页后删除按钮隐藏。
        vm.LightSchemeField.Selected = "shipped_light";
        vm.EditLightSchemeCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.False(page.GetVisualDescendants().OfType<Ursa.Controls.IconButton>()
            .Single(b => b.Classes.Contains("Danger")).IsVisible);

        window.Close();
    }
}
