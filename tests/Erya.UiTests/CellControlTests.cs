using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya.Controls;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// Cell / CellGroup / ValidationErrorIcon 的结构级验收：模板套用、组内分隔线、
/// 方位圆角、Detail 点开/收起与内部点击拦截、校验错误指示，以及示例组合的渲染。
/// </summary>
public class CellControlTests
{
    private static T ShowInWindow<T>(T content) where T : Control
    {
        var window = new Window { Width = 520, Height = 720, Content = content };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return content;
    }

    private static void Click(Control control)
        => control.RaiseEvent(new RoutedEventArgs(Button.ClickEvent, control));

    /// <summary>与 demo 一致：SimpleToggleSwitch 主题 + 轨道内「开/关」文字。</summary>
    private static ToggleSwitch LabeledSwitch(bool isChecked = false)
    {
        var sw = new ToggleSwitch { OnContent = "开", OffContent = "关", IsChecked = isChecked };
        var app = Application.Current!;
        if (app.TryGetResource("SimpleToggleSwitch", app.ActualThemeVariant, out var theme)
            && theme is ControlTheme ct)
        {
            sw.Theme = ct;
        }
        return sw;
    }

    private sealed class PageSizeModel
    {
        public int PageSize { get; set; } = 5;
    }

    [AvaloniaFact]
    public void Cell_theme_applies_and_drawes_separators_between_group_rows()
    {
        var c1 = new Cell { Content = "第一行" };
        var c2 = new Cell { Content = "第二行" };
        var group = new CellGroup { Header = "分组" };
        group.Items.Add(c1);
        group.Items.Add(c2);
        ShowInWindow(group);

        Assert.NotNull(c1.Template); // ControlTheme 解析成功
        Assert.Contains(group.GetVisualDescendants(),
            v => v is Border { Name: "PART_Root" });

        var separators = group.GetVisualDescendants()
            .OfType<Border>()
            .Where(b => b.Name == "PART_Separator")
            .ToList();
        Assert.Equal(2, separators.Count);
        Assert.True(separators[0].IsVisible);   // 行间分隔线
        Assert.False(separators[1].IsVisible);  // 最后一行不画
    }

    [AvaloniaFact]
    public void Row_corner_radii_align_with_group_card_edges()
    {
        var c1 = new Cell { Content = "第一行" };
        var c2 = new Cell { Content = "第二行" };
        var c3 = new Cell { Content = "第三行" };
        var group = new CellGroup(); // 默认 CornerRadius = SemiBorderRadiusMedium = 6
        group.Items.Add(c1);
        group.Items.Add(c2);
        group.Items.Add(c3);
        ShowInWindow(group);

        Assert.Equal(new CornerRadius(6, 6, 0, 0), c1.CornerRadius); // 首行只圆上方
        Assert.Equal(new CornerRadius(0), c2.CornerRadius);          // 中间行方角
        Assert.Equal(new CornerRadius(0, 0, 6, 6), c3.CornerRadius); // 末行只圆下方
    }

    [AvaloniaFact]
    public void Detail_cell_click_toggles_expansion_and_chevron()
    {
        var cell = new Cell { Content = "模糊音", Detail = new TextBlock { Text = "详细设置" } };
        var group = new CellGroup();
        group.Items.Add(cell);
        ShowInWindow(group);

        var chevron = cell.GetVisualDescendants().OfType<PathIcon>()
            .Single(p => p.Name == "PART_Chevron");
        var detailHost = cell.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "PART_DetailHost");

        Assert.False(cell.IsExpanded);
        Assert.True(chevron.IsVisible);        // Detail 行自带箭头（收起时向下）
        Assert.False(detailHost.IsVisible);
        Assert.Equal(0, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);

        Click(cell);
        Assert.True(cell.IsExpanded);
        Assert.True(detailHost.IsVisible);
        Assert.Equal(180, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle); // 展开后向上

        Click(cell);
        Assert.False(cell.IsExpanded);
        Assert.False(detailHost.IsVisible);
        Assert.Equal(0, Assert.IsType<RotateTransform>(chevron.RenderTransform).Angle);
    }

    [AvaloniaFact]
    public void Expanded_detail_row_drops_hover_corner_radius()
    {
        var cell = new Cell { Content = "行", Detail = new TextBlock { Text = "详情" } };
        var group = new CellGroup();
        group.Items.Add(cell);
        ShowInWindow(group);

        var root = cell.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "PART_Root");
        Assert.Equal(new CornerRadius(6), root.CornerRadius); // 唯一行继承卡片全圆角

        cell.IsExpanded = true; // 展开后下方连着详情区，高亮应为方角
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new CornerRadius(0), root.CornerRadius);

        cell.IsExpanded = false;
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(new CornerRadius(6), root.CornerRadius);
    }

    [AvaloniaFact]
    public void Clicks_inside_detail_do_not_collapse_the_cell()
    {
        var box = new CheckBox { Content = "平翘舌" };
        var cell = new Cell
        {
            Content = "模糊音",
            Detail = new StackPanel { Children = { box } },
        };
        var group = new CellGroup();
        group.Items.Add(cell);
        ShowInWindow(group);

        cell.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(cell.IsExpanded);

        Click(box); // 详情区域内的点击不作为“点击整行”
        Assert.True(cell.IsExpanded);

        Click(cell); // 行首点击才收起
        Assert.False(cell.IsExpanded);
    }

    [AvaloniaFact]
    public void Pressing_key_gesture_input_inside_detail_focuses_it_and_keeps_row_open()
    {
        // 真实输入管线：按下事件在详情区域内终结，行按钮收不到按压；
        // 焦点转发给 KeyGestureInput（它自己不处理按压）。
        var input = new Ursa.Controls.KeyGestureInput { Width = 230 };
        var cell = new Cell
        {
            Content = "方案选单快捷键",
            Detail = new StackPanel { Children = { input } },
        };
        var group = new CellGroup();
        group.Items.Add(cell);
        var window = new Window { Width = 480, Height = 320, Content = group };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        cell.IsExpanded = true;
        Dispatcher.UIThread.RunJobs();

        var root = cell.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "PART_Root");
        var onInput = input.TranslatePoint(new Point(40, 10), window)!.Value;
        window.MouseDown(onInput, Avalonia.Input.MouseButton.Left);
        window.MouseUp(onInput, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.True(cell.IsExpanded); // 不收起
        Assert.Same(input, window.FocusManager?.GetFocusedElement()); // 且获得焦点

        var onHeader = root.TranslatePoint(new Point(60, 10), window)!.Value;
        window.MouseDown(onHeader, Avalonia.Input.MouseButton.Left);
        window.MouseUp(onHeader, Avalonia.Input.MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        Assert.False(cell.IsExpanded); // 行首点击才收起
    }

    [AvaloniaFact]
    public void Plain_cell_without_detail_shows_no_chevron_and_never_expands()
    {
        var cell = new Cell { Content = "普通行" };
        ShowInWindow(cell);

        var chevron = cell.GetVisualDescendants().OfType<PathIcon>()
            .Single(p => p.Name == "PART_Chevron");
        Assert.False(chevron.IsVisible);
        Assert.False(cell.ShowChevron);

        Click(cell);
        Assert.False(cell.IsExpanded);
    }

    [AvaloniaFact]
    public void CellGroup_wraps_raw_models_into_cells()
    {
        var group = new CellGroup();
        group.Items.Add("标题甲");
        group.Items.Add("标题乙");
        ShowInWindow(group);

        var titles = group.GetVisualDescendants()
            .OfType<Cell>()
            .Select(c => c.Content as string)
            .ToList();
        Assert.Equal(["标题甲", "标题乙"], titles);
    }

    [AvaloniaFact]
    public void Validation_error_icon_follows_target_errors()
    {
        var target = new TextBox();
        var icon = new ValidationErrorIcon { Target = target };
        ShowInWindow(new StackPanel { Children = { target, icon } });

        var root = icon.GetVisualDescendants().OfType<Border>()
            .Single(b => b.Name == "PART_Root");
        Assert.False(icon.HasErrors);
        Assert.False(root.IsVisible); // 无错时零占位

        DataValidationErrors.SetError(target, new InvalidOperationException("请输入 3–10 之间的数字"));
        Dispatcher.UIThread.RunJobs();
        Assert.True(icon.HasErrors);
        Assert.Equal("请输入 3–10 之间的数字", icon.Message);
        Assert.True(root.IsVisible);

        DataValidationErrors.ClearErrors(target);
        Dispatcher.UIThread.RunJobs();
        Assert.False(icon.HasErrors);
        Assert.False(root.IsVisible);
    }

    [AvaloniaFact]
    public void Cleared_numeric_binds_quietly_and_shows_error_state()
    {
        var nu = new NumericUpDown { Minimum = 3, Maximum = 10, Width = 130 };
        nu.Classes.Add("silent-errors");
        nu[!NumericUpDown.ValueProperty] = new Binding("PageSize")
        {
            Source = new PageSizeModel(),
            Mode = BindingMode.TwoWay,
        };
        ShowInWindow(nu);

        // silent-errors：模板内不再渲染默认的错误文字区
        Assert.DoesNotContain(nu.GetVisualDescendants(),
            v => v.Name == "InlineDataValidationContentControl");

        // 清空文本（decimal? Value → null → int 绑定失败）→ 进入错误状态
        Assert.Equal("5", nu.Text);
        nu.Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.True(DataValidationErrors.GetHasErrors(nu));
    }

    [AvaloniaFact]
    public void Demo_composition_renders_for_visual_review()
    {
        // 与 demo/Erya.Controls.Demo 的主窗口同构的组合，做渲染与截图验收。
        var fuzzyDetail = new StackPanel { Spacing = 10 };
        foreach (var label in new[] { "z / zh（平翘舌）", "c / ch", "s / sh", "an / ang（前后鼻音）", "en / eng", "h / f" })
        {
            fuzzyDetail.Children.Add(new CheckBox { Content = label, IsChecked = label.StartsWith('z') });
        }
        var fuzzy = new Cell
        {
            Content = "模糊音",
            Description = "z↔zh、c↔ch、s↔sh，前后鼻音等",
            Note = "已启用 1 项",
            IsExpanded = true,
            Detail = fuzzyDetail,
        };
        var radioDetail = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new RadioButton { Content = "中文", GroupName = "demo-initial", IsChecked = true },
                new RadioButton { Content = "英文", GroupName = "demo-initial" },
            },
        };
        var initialMode = new Cell { Content = "初始输入状态", Detail = radioDetail };
        var nu = new NumericUpDown { Minimum = 3, Maximum = 10, Width = 130 };
        nu.Classes.Add("silent-errors"); // 与 demo 一致：错误只显示右侧感叹号
        nu[!NumericUpDown.ValueProperty] = new Binding("PageSize")
        {
            Source = new PageSizeModel(),
            Mode = BindingMode.TwoWay,
        };
        var numericRow = new Cell
        {
            Content = "候选词个数",
            RightContent = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                Spacing = 8,
                Children = { nu, new ValidationErrorIcon { Target = nu } },
            },
        };
        var toggleRow = new Cell
        {
            Content = "切换时提交英文",
            RightContent = LabeledSwitch(),
        };
        var textRow = new Cell
        {
            Content = "自定义短语",
            Description = "文本输入示例",
            RightContent = new TextBox { Text = "/sl", Width = 180 },
        };
        var comboRow = new Cell
        {
            Content = "候选排序",
            RightContent = new ComboBox
            {
                SelectedIndex = 0,
                MinWidth = 150,
                Items = { "按输入频率", "按字典序", "最近优先" },
            },
        };
        var sliderRow = new Cell
        {
            Content = "候选文字字号",
            Note = "14 pt",
            RightContent = new Slider { Minimum = 9, Maximum = 28, Value = 14, Width = 180 },
        };
        var navRow = new Cell
        {
            Content = "高级设置",
            Note = "键位、日志",
            ShowChevron = true,
        };

        var root = new StackPanel
        {
            Spacing = 20,
            Margin = new Thickness(24, 18, 24, 24),
            Width = 560,
        };
        var basicGroup = new CellGroup { Header = "基础输入" };
        basicGroup.Items.Add(toggleRow);
        basicGroup.Items.Add(numericRow);
        var inputsGroup = new CellGroup { Header = "更多输入控件" };
        inputsGroup.Items.Add(textRow);
        inputsGroup.Items.Add(comboRow);
        inputsGroup.Items.Add(sliderRow);
        var detailGroup = new CellGroup { Header = "行内详情" };
        detailGroup.Items.Add(fuzzy);
        detailGroup.Items.Add(initialMode);
        var moreGroup = new CellGroup { Header = "更多" };
        moreGroup.Items.Add(navRow);
        root.Children.Add(basicGroup);
        root.Children.Add(inputsGroup);
        root.Children.Add(detailGroup);
        root.Children.Add(moreGroup);

        var window = new Window { Width = 620, Height = 900, Content = root };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 清空数值让红色感叹号出现在截图里。
        nu.Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.True(DataValidationErrors.GetHasErrors(nu));

        CaptureFrame(window, "/tmp/erya-cell-demo-light.png", ThemeVariant.Light);
        CaptureFrame(window, "/tmp/erya-cell-demo-dark.png", ThemeVariant.Dark);
        window.Close();
    }

    private static void CaptureFrame(Window window, string path, ThemeVariant variant)
    {
        var restore = Application.Current!.RequestedThemeVariant;
        Application.Current.RequestedThemeVariant = variant;
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(path);
        Application.Current.RequestedThemeVariant = restore;
        Dispatcher.UIThread.RunJobs();
    }
}
