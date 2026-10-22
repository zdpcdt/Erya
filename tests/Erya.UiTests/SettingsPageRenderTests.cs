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
    public async Task Skin_gallery_applies_light_scheme_and_edit_opens_dialog()
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

        // 画廊：两套皮肤缩略卡 + 亮/暗主题两个 Cell（带图标，Note 不再重复显示皮肤名）。
        var cards = page.GetVisualDescendants().OfType<Button>()
            .Where(b => (ToolTip.GetTip(b) as string) == "应用为亮色主题").ToList();
        Assert.Equal(2, cards.Count);
        var lightCell = page.GetVisualDescendants().OfType<Cell>().Single(c => (c.Content as string) == "亮色主题");
        var darkCell = page.GetVisualDescendants().OfType<Cell>().Single(c => (c.Content as string) == "暗色主题");
        Assert.Null(lightCell.Note); // Note 不再重复显示皮肤名（下拉里已有）
        Assert.Null(darkCell.Note);

        // 固定区：预览宿主与槽位行不在滚动区里——滚画廊不动锚点。
        Assert.All(page.GetVisualDescendants().OfType<ContentControl>()
            .Where(cc => cc.Content is CandidatePreviewViewModel),
            h => Assert.DoesNotContain(h.GetVisualAncestors(), a => a is ScrollViewer));
        Assert.DoesNotContain(lightCell.GetVisualAncestors(), a => a is ScrollViewer);
        Assert.DoesNotContain(darkCell.GetVisualAncestors(), a => a is ScrollViewer);

        // 点卡片 = 应用为亮色主题（待保存编辑）；角上 ☾ = 应用为暗色；角色角标联动。
        var userDark = editor.SchemeLibrary!.Schemes.Single(s => s.Name == "user_dark");
        vm.UseAsLightSchemeCommand.Execute(userDark);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("user_dark", vm.LightSchemeField!.Selected);
        Assert.True(vm.IsDirty);
        Assert.True(userDark.IsCurrentLight);
        Assert.False(userDark.IsCurrentDark);
        // 当前角色用 Ursa Badge 角标显示在卡片上。
        Assert.Contains(page.GetVisualDescendants().OfType<Ursa.Controls.Badge>(),
            b => b.Content as string == "亮色" && b.IsVisible);
        Assert.DoesNotContain(page.GetVisualDescendants().OfType<Ursa.Controls.Badge>(),
            b => b.Content as string == "暗色" && b.IsVisible);
        vm.UseAsDarkSchemeCommand.Execute(userDark);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("user_dark", vm.DarkSchemeField!.Selected);
        Assert.True(userDark.IsCurrentDark);
        vm.LightSchemeField.Selected = "macos_light"; // 还原，后续断言用
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.SchemeLibrary.Schemes.Single(s => s.Name == "macos_light").IsCurrentLight);
        Assert.False(userDark.IsCurrentLight);

        // 画廊：自定义置顶 + 搜索过滤。
        Assert.Equal(["user_dark", "macos_light"], vm.FilteredSchemes.Select(s => s.Name).ToList());
        vm.SearchText = "macos";
        Assert.Equal(["macos_light"], vm.FilteredSchemes.Select(s => s.Name).ToList());
        vm.SearchText = "";

        // 编辑语义（不经 OverlayDialog，直接构造对话框 VM）：
        // 内置皮肤「打开即副本」——对话框编辑的是未入库草稿，确定=入库，原内置从未被碰。
        var fields = editor.Sections.SelectMany(s => s.Fields).ToList();
        var original = editor.SchemeLibrary.Schemes.Single(s => s.Name == "macos_light");
        var draft = SkinPageViewModel.CreateDraft(original);
        Assert.Equal("我的皮肤", draft.Name);
        Assert.Equal("ECECEC", draft.BackColor); // 配色以内置为底
        var dialog = new SchemeEditDialogViewModel(draft, fields, editor.SchemeLibrary,
            isSquirrel: true, isDraft: true);
        Assert.True(dialog.IsDraft);
        Assert.Equal("新建皮肤", dialog.Title);
        dialog.Scheme.BackColor = "1C1C1E";
        dialog.Scheme.Name = "我的浅色改";
        var saved = dialog.Commit();
        Assert.Equal("我的浅色改", saved!.Name);
        Assert.False(saved.IsShipped);
        Assert.Equal("1C1C1E", saved.BackColor);
        Assert.Equal("ECECEC", original.BackColor); // 内置皮肤从未被修改
        Assert.Contains(editor.SchemeLibrary.Schemes, s => s.Name == "我的浅色改");
        Assert.True(vm.IsDirty);

        // 草稿取消 = 丢弃（无还原动作、不进库）。
        var draftDrop = SkinPageViewModel.CreateDraft(original);
        var dialogDrop = new SchemeEditDialogViewModel(draftDrop, fields, editor.SchemeLibrary,
            isSquirrel: true, isDraft: true);
        dialogDrop.Scheme.BackColor = "000000";
        dialogDrop.Revert();
        Assert.DoesNotContain(editor.SchemeLibrary.Schemes, s => s.Name == draftDrop.Name);

        // 空白新建草稿：默认浅色。
        Assert.Equal("我的皮肤", SkinPageViewModel.CreateDraft(null).Name);

        // 已有自定义皮肤：确定=保留同一实例，取消=还原开框快照。
        var dialog2 = new SchemeEditDialogViewModel(userDark, fields, editor.SchemeLibrary, isSquirrel: true);
        Assert.False(dialog2.IsDraft);
        dialog2.Scheme.BackColor = "000000";
        dialog2.Revert();
        Assert.Equal("1C1C1E", userDark.BackColor);
        Assert.Same(userDark, dialog2.Commit());

        // 对话框控件本体：预览 + 8 个颜色块（无 0x 色号文字）+ 鼠须管下显示字体与排版组。
        var control = new SchemeEditDialog { DataContext = dialog2, Width = 600, Height = 700 };
        var host = new Window { Width = 640, Height = 760, Content = control };
        host.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(8, control.GetVisualDescendants().OfType<ColorSwatch>().Count());
        Assert.DoesNotContain(control.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").StartsWith("0x"));
        Assert.DoesNotContain(control.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains("作者"));
        Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "ni hao");
        Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains("字体与排版"));
        // 色块显示当前色（BGR 十六进制 → RGB 画刷），改值实时反映。
        var swatch = control.GetVisualDescendants().OfType<ColorSwatch>().First();
        var swatchBorder = swatch.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "PART_Swatch");
        var swatchColor = Assert.IsAssignableFrom<ISolidColorBrush>(swatchBorder.Background).Color;
        Assert.Equal(0x1E, swatchColor.R); // user_dark back_color 0x1C1C1E（BGR）→ R=1E
        dialog2.Scheme.BackColor = "FFFFFF";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(0xFF, Assert.IsAssignableFrom<ISolidColorBrush>(swatchBorder.Background).Color.R);

        // 真实点击色块弹出扁平取色面板（ColorView，Popup 挂在窗口 OverlayPopupHost），
        // 在面板里改色实时写回皮肤（BGR 十六进制）。
        var swatchButton = swatch.GetVisualDescendants().OfType<Button>().First();
        var at = swatchButton.TranslatePoint(new Point(swatchButton.Bounds.Width / 2, swatchButton.Bounds.Height / 2), host)!.Value;
        host.MouseDown(at, Avalonia.Input.MouseButton.Left);
        host.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var picker = host.GetVisualDescendants().OfType<ColorView>().FirstOrDefault();
        Assert.NotNull(picker);
        picker!.Color = Avalonia.Media.Color.FromRgb(0x11, 0x22, 0x33);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("332211", dialog2.Scheme.BackColor); // RGB(11,22,33) → BGR 十六进制 332211
        host.Close();

        // 小狼毫：字体与排版组不显示（皮肤级排版对它是 placebo）。
        var weaselDialog = new SchemeEditDialogViewModel(original, fields, editor.SchemeLibrary, isSquirrel: false);
        var control2 = new SchemeEditDialog { DataContext = weaselDialog, Width = 600, Height = 700 };
        var host2 = new Window { Width = 640, Height = 760, Content = control2 };
        host2.Show();
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(control2.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains("字体与排版"));
        host2.Close();

        // 以此新增：内置派生成自定义皮肤（唯一名）。
        editor.SchemeLibrary.DuplicateSchemeCommand.Execute(editor.SchemeLibrary.Schemes.Single(s => s.Name == "macos_light"));
        Assert.Contains(editor.SchemeLibrary.Schemes, s => s.Name == "macos_light1");

        Capture(window, Path.Combine(Path.GetTempPath(), "erya-preview-light.png"), ThemeVariant.Light);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Scheme_delete_button_is_wired_on_the_gallery_card()
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

        // 自定义卡的删除按钮必须携带皮肤作 CommandParameter——否则命令收到
        // null 静默不动（「删除皮肤没有反应」回归）。
        List<Ursa.Controls.IconButton> DeleteButtons() => page.GetVisualDescendants().OfType<Ursa.Controls.IconButton>()
            .Where(b => (ToolTip.GetTip(b) as string) == "删除此皮肤").ToList();
        var userDark = editor.SchemeLibrary!.Schemes.Single(s => s.Name == "user_dark");
        var delete = DeleteButtons().Single(b => b.IsVisible);
        Assert.Same(userDark, delete.CommandParameter);
        delete.Command!.Execute(delete.CommandParameter);
        Dispatcher.UIThread.RunJobs();
        Assert.DoesNotContain(editor.SchemeLibrary.Schemes, s => s.Name == userDark.Name);

        // 内置卡：删除按钮在树上但隐藏；复制 YAML 按钮对所有卡可用
        //（删除后只剩内置卡，1 个）。
        Assert.All(DeleteButtons(), b => Assert.False(b.IsVisible));
        var copyYamlButtons = page.GetVisualDescendants().OfType<Ursa.Controls.IconButton>()
            .Where(b => (ToolTip.GetTip(b) as string) == "复制 YAML 定义").ToList();
        Assert.Single(copyYamlButtons);

        window.Close();
    }
}
