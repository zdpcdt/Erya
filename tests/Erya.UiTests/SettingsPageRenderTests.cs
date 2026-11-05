using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Ursa.Controls;
using Erya.Core.Distributions;
using Erya.ViewModels.Settings;
using Erya.Views;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 设置页（SettingsCard 版）的端到端渲染：schema 字段渲染成分组卡片里的 Cell 行、
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
            switcher:
              hotkeys:
                - Control+grave
                - F4
            """);
        Write("default.custom.yaml", "patch:\n  menu/page_size: 9\n");

        var vm = new SettingsEditorViewModel(new StubDistribution(_dir), "default");
        await vm.LoadCommand.ExecuteAsync(null);
        Assert.True(vm.IsSupported);

        var (_, page, window) = Show(vm);

        // 每个 schema 分节一张卡片；字段渲染成 SettingsCard 行。
        // （皮肤库容器在 default 目标下由父 StackPanel 折叠，需按“有效可见”过滤。）
        var groups = page.GetVisualDescendants().OfType<SettingsCardGroup>()
            .Where(IsEffectivelyVisible).ToList();
        Assert.Equal(vm.Sections.Count, groups.Count);
        var cards = page.GetVisualDescendants().OfType<SettingsCard>().ToList();
        Assert.Equal(vm.Sections.Sum(s => s.Fields.Count), cards.Count);
        Assert.True(cards.Count >= 10);

        // Detail 行：展开后详情可见，行内点击收起。
        // （按 TemplatedParent 过滤：详情里嵌套的 SettingsCard 各自带一个同名宿主。）
        var detailCard = cards.First(c => c.Detail is not null);
        detailCard.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(detailCard.GetVisualDescendants()
            .OfType<ContentPresenter>().Single(p => p.Name == "PART_DetailHost"
                                                     && ReferenceEquals(p.TemplatedParent, detailCard)).IsVisible);

        // 快捷键行：视觉顺序与字段顺序一致；➕ 追加一排空行并自动展开
        // （IsExpanded 绑定 IsDetailExpanded），行内用 KeyGestureInput 录入。
        var fields = vm.Sections.SelectMany(s => s.Fields).ToList();
        var hotkeyCard = cards[fields.FindIndex(f => f is ListFieldViewModel)];
        fields.OfType<ListFieldViewModel>().Single().AddHotkeyRowCommand.Execute(null);
        Dispatcher.UIThread.RunJobs();
        Assert.Contains(hotkeyCard.GetVisualDescendants(), d => d is Ursa.Controls.KeyGestureInput);

        // 记忆选项行也展开，截屏才能拍到编辑区。
        cards[fields.FindIndex(f => f is CheckListFieldViewModel)].IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        // 键位映射：左列按键名按快捷键输入框同款风格显示（Caps_Lock → CapsLock），
        // 不再出现 RIME 原始 token。
        var mappingCard = cards[fields.FindIndex(f => f is KeyMappingFieldViewModel)];
        mappingCard.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        var keyTexts = mappingCard.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => !string.IsNullOrEmpty(t.Text)).Select(t => t.Text!).ToList();
        Assert.Contains("CapsLock", keyTexts);
        Assert.DoesNotContain("Caps_Lock", keyTexts);

        // 滚到键位映射行：它在分节里最靠下，滚到它，上方的快捷键/记忆选项也在视野内。
        mappingCard.BringIntoView();
        Dispatcher.UIThread.RunJobs();

        Capture(window, Path.Combine(Path.GetTempPath(), "erya-settings-input-light.png"), ThemeVariant.Light);
        Capture(window, Path.Combine(Path.GetTempPath(), "erya-settings-input-dark.png"), ThemeVariant.Dark);
        window.Close();
    }

    [AvaloniaFact]
    public async Task Clicking_add_hotkey_button_appends_row_and_keeps_detail_expanded()
    {
        Write("default.yaml", """
            switcher:
              hotkeys:
                - Control+grave
            """);
        var vm = new SettingsEditorViewModel(new StubDistribution(_dir), "default");
        await vm.LoadCommand.ExecuteAsync(null);

        var (_, page, window) = Show(vm);
        var list = vm.Sections.SelectMany(s => s.Fields)
            .OfType<ListFieldViewModel>().Single();

        void ClickAdd()
        {
            // 真实指针路径点外层 ➕：header 内嵌按钮的事件不得被卡片的展开/收起
            // toggle 抢走（点 ➕ 必须加行并展开，而不是翻转收起）。
            var add = page.GetVisualDescendants().OfType<Ursa.Controls.IconButton>()
                .Single(b => ReferenceEquals(b.Command, list.AddHotkeyRowCommand));
            var center = add.TranslatePoint(new Point(add.Bounds.Width / 2, add.Bounds.Height / 2), window)!.Value;
            window.MouseMove(center);
            window.MouseDown(center, Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
            window.MouseUp(center, Avalonia.Input.MouseButton.Left);
            Dispatcher.UIThread.RunJobs();
        }

        string FocusedRowValue() =>
            Assert.IsType<Erya.ViewModels.Settings.HotkeyRowViewModel>(
                page.GetVisualDescendants().OfType<Ursa.Controls.KeyGestureInput>()
                    .Single(g => g.IsFocused).DataContext).Value;

        // 第一次点 ➕：追加空行、展开并聚焦。
        ClickAdd();
        Assert.Equal(["Control+grave", ""], list.Items.Select(i => i.Value));
        Assert.True(list.IsDetailExpanded);
        Assert.Equal("", FocusedRowValue());
        Assert.False(list.Items[^1].IsFresh); // 聚焦标志已复位

        // 第二次点 ➕（空行还在）：不叠行，聚焦回那排空行（「继续录」入口）。
        ClickAdd();
        Assert.Equal(["Control+grave", ""], list.Items.Select(i => i.Value));
        Assert.Equal("", FocusedRowValue());

        // 录入后再点 ➕：加第三行并聚焦新行。
        list.Items[1].Value = "F4";
        ClickAdd();
        Assert.Equal(["Control+grave", "F4", ""], list.Items.Select(i => i.Value));
        Assert.Equal("", FocusedRowValue());
        window.Close();
    }

    [AvaloniaFact]
    public async Task Clicking_row_clear_button_deletes_that_row()
    {
        Write("default.yaml", """
            switcher:
              hotkeys:
                - Control+grave
                - F4
            """);
        var vm = new SettingsEditorViewModel(new StubDistribution(_dir), "default");
        await vm.LoadCommand.ExecuteAsync(null);

        var (_, page, window) = Show(vm);
        var list = vm.Sections.SelectMany(s => s.Fields)
            .OfType<ListFieldViewModel>().Single();
        list.IsDetailExpanded = true;
        Dispatcher.UIThread.RunJobs();

        // 鼠标点行内 ×（PART_ClearButton，hover 才显示）：HotkeyRowDelete 拦截
        // Click → DeleteRow 删整行。这条链路依赖 Ursa 主题 part 名，part 改名
        // 时此测试先红，避免删行功能静默退化成只有键盘清空。
        var firstRow = page.GetVisualDescendants().OfType<Ursa.Controls.KeyGestureInput>()
            .First(g => g.DataContext is Erya.ViewModels.Settings.HotkeyRowViewModel { Value: "Control+grave" });
        var hover = firstRow.TranslatePoint(new Point(firstRow.Bounds.Width / 2, firstRow.Bounds.Height / 2), window)!.Value;
        window.MouseMove(hover); // pointerover → × 显示并布局
        Dispatcher.UIThread.RunJobs();
        var clear = firstRow.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == "PART_ClearButton");
        var center = clear.TranslatePoint(new Point(clear.Bounds.Width / 2, clear.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center);
        window.MouseDown(center, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(center, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["F4"], list.Items.Select(i => i.Value));
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
        // （「亮色皮肤」文字同时是预览带标签，字段按 SettingsCard 判别）。
        Assert.Contains(texts, t => t == "排列方式");
        Assert.Contains(texts, t => t == "间距");
        var cards = page.GetVisualDescendants().OfType<SettingsCard>().Select(c => c.Content as string).ToList();
        Assert.Contains("候选字体", cards);
        Assert.DoesNotContain(cards, t => t == "亮色皮肤" || t == "应用列表");

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

        // 画廊：两套皮肤缩略卡 + 亮/暗主题两个 SettingsCard（带图标，Note 不再重复显示皮肤名）。
        var cards = page.GetVisualDescendants().OfType<Button>()
            .Where(b => (ToolTip.GetTip(b) as string) == "应用为亮色主题").ToList();
        Assert.Equal(2, cards.Count);
        var lightCard = page.GetVisualDescendants().OfType<SettingsCard>().Single(c => (c.Content as string) == "亮色主题");
        var darkCard = page.GetVisualDescendants().OfType<SettingsCard>().Single(c => (c.Content as string) == "暗色主题");
        Assert.Null(lightCard.Note); // Note 不再重复显示皮肤名（下拉里已有）
        Assert.Null(darkCard.Note);

        // 固定区：预览宿主与槽位行不在滚动区里——滚画廊不动锚点。
        Assert.All(page.GetVisualDescendants().OfType<ContentControl>()
            .Where(cc => cc.Content is CandidatePreviewViewModel),
            h => Assert.DoesNotContain(h.GetVisualAncestors(), a => a is ScrollViewer));
        Assert.DoesNotContain(lightCard.GetVisualAncestors(), a => a is ScrollViewer);
        Assert.DoesNotContain(darkCard.GetVisualAncestors(), a => a is ScrollViewer);

        // 点卡片 = 应用为亮色主题（待保存编辑）；角上 ☾ = 应用为暗色；角色角标联动。
        var userDark = editor.SchemeLibrary!.Schemes.Single(s => s.Name == "user_dark");
        vm.UseAsLightSchemeCommand.Execute(userDark);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("user_dark", vm.LightSchemeField!.Selected);
        Assert.True(vm.IsDirty);
        Assert.True(userDark.IsCurrentLight);
        Assert.False(userDark.IsCurrentDark);
        // 当前角色用卡片描边高亮：当前亮色=主色 2px、暗色=紫 2px、普通=1px。
        var cardBorders = page.GetVisualDescendants().OfType<Border>()
            .Where(b => b.Classes.Contains("skin-card")).ToList();
        Assert.Equal(2, cardBorders.Count);
        var userCard = cardBorders.Single(b => ReferenceEquals(b.DataContext, userDark));
        Assert.Equal(new Thickness(2), userCard.BorderThickness);
        Assert.Equal(0x00, Assert.IsAssignableFrom<ISolidColorBrush>(userCard.BorderBrush).Color.R); // SemiColorPrimary
        var plainCard = cardBorders.Single(b => !ReferenceEquals(b.DataContext, userDark));
        Assert.Equal(new Thickness(1), plainCard.BorderThickness);
        vm.LightSchemeField.Selected = "macos_light"; // 还原，后续断言用
        Dispatcher.UIThread.RunJobs();
        Assert.True(editor.SchemeLibrary.Schemes.Single(s => s.Name == "macos_light").IsCurrentLight);
        Assert.False(userDark.IsCurrentLight);

        // 仅作暗色时：紫色 2px 描边（亮色优先级更高，同挂两角色时显示主色）。
        // 断言时重新取容器——列表若重建，早前捕获的引用是脱挂的旧 Border。
        vm.UseAsDarkSchemeCommand.Execute(userDark);
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("user_dark", vm.DarkSchemeField!.Selected);
        Assert.True(userDark.IsCurrentDark);
        var liveCard = page.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Classes.Contains("skin-card") && ReferenceEquals(b.DataContext, userDark));
        Assert.Equal(new Thickness(2), liveCard.BorderThickness);
        Assert.Equal(0x7C, Assert.IsAssignableFrom<ISolidColorBrush>(liveCard.BorderBrush).Color.R); // 暗色紫描边

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
        Assert.Equal(8, control.GetVisualDescendants().OfType<ColorPicker>().Count());
        Assert.DoesNotContain(control.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").StartsWith("0x"));
        Assert.DoesNotContain(control.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains("作者"));
        Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), t => t.Text == "ni hao");
        Assert.Contains(control.GetVisualDescendants().OfType<TextBlock>(), t => (t.Text ?? "").Contains("字体与排版"));
        // 取色器显示当前色（BGR 十六进制 → RGB Color，经 RimeColor 转换器），改值实时反映。
        var picker = control.GetVisualDescendants().OfType<ColorPicker>().First();
        Assert.Equal(Color.FromRgb(0x1E, 0x1C, 0x1C), picker.Color); // user_dark back_color 0x1C1C1E（BGR）→ R=1E
        dialog2.Scheme.BackColor = "FFFFFF";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(Color.FromRgb(0xFF, 0xFF, 0xFF), picker.Color);

        // 真实点击弹出下拉取色面板（Popup 挂在窗口 OverlayPopupHost），
        // 在面板色谱上选色实时写回皮肤（BGR 十六进制）。
        var dropDown = picker.GetVisualDescendants().OfType<DropDownButton>().First();
        var at = dropDown.TranslatePoint(new Point(dropDown.Bounds.Width / 2, dropDown.Bounds.Height / 2), host)!.Value;
        host.MouseDown(at, Avalonia.Input.MouseButton.Left);
        host.MouseUp(at, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        var spectrum = host.GetVisualDescendants().OfType<ColorSpectrum>().FirstOrDefault();
        Assert.NotNull(spectrum);
        spectrum!.HsvColor = new HsvColor(1, 210, 2.0 / 3, 0.2); // ≈ RGB(11,22,33)
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
