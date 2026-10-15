using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Metadata;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;

namespace Erya.Controls;

/// <summary>
/// 设置中心风格的一行：左侧图标（Icon）+ 标题（Content）+ 说明（Description），
/// 右侧当前值（Note）与操作区（RightContent，放 ToggleSwitch、RadioButton、
/// NumericUpDown 等），行尾可带指示箭头。整行继承 Button：绑定 Command 时点击
/// 整行触发命令；设置 Detail 时点击行首区域展开/收起内嵌的详细设置。
/// Detail 行的箭头收起时向下、展开时向上；详情区域内的点击与悬停
/// （多选框、按钮、空白）不会触发整行的收缩/悬停高亮。
/// 默认样式见 CellTheme.axaml（Semi 风格，配 CellGroup 使用）。
/// </summary>
[PseudoClasses(":expanded", ":has-detail", ":over-detail")]
public class Cell : Button
{
    public const string PART_DetailHost = "PART_DetailHost";

    private const string ClassExpanded = ":expanded";
    private const string ClassHasDetail = ":has-detail";
    private const string ClassOverDetail = ":over-detail";

    private Control? _detailHost;

    public Cell()
    {
        Click += OnCellClick;
    }

    private void OnCellClick(object? sender, RoutedEventArgs e)
    {
        if (Detail is null) return;
        // 详情区域内的交互（多选框、按钮等）不作为“点击整行”处理。
        if (e.Source is Visual source && _detailHost is { } host && host.IsVisualAncestorOf(source))
        {
            return;
        }
        IsExpanded = !IsExpanded;
    }

    protected override void OnApplyTemplate(Avalonia.Controls.Primitives.TemplateAppliedEventArgs e)
    {
        if (_detailHost is { } old)
        {
            old.PointerEntered -= OnDetailPointerEntered;
            old.PointerExited -= OnDetailPointerExited;
            old.RemoveHandler(InputElement.PointerPressedEvent, OnDetailPointerPressed);
        }
        base.OnApplyTemplate(e);
        _detailHost = e.NameScope.Find(PART_DetailHost) as Control;
        if (_detailHost is { } neu)
        {
            neu.PointerEntered += OnDetailPointerEntered;
            neu.PointerExited += OnDetailPointerExited;
            // 按下事件在详情区域内终结，不冒泡给行按钮（否则 KeyGestureInput 等
            // 不自行处理按压的控件会触发行的按下-抬起，导致行收起）。
            neu.AddHandler(InputElement.PointerPressedEvent, OnDetailPointerPressed);
        }
    }

    private void OnDetailPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        // 给按下位置最近的可聚焦控件（KeyGestureInput 等）转发焦点——
        // 这些控件自己不处理按压，被拦掉冒泡后仍要能用键盘录入。
        if (e.Source is Visual source && _detailHost is { } host)
        {
            source.GetSelfAndVisualAncestors()
                .OfType<InputElement>()
                .TakeWhile(i => !ReferenceEquals(i, host))
                .FirstOrDefault(i => i.Focusable)
                ?.Focus(Avalonia.Input.NavigationMethod.Pointer);
        }
        e.Handled = true;
    }

    private void OnDetailPointerEntered(object? sender, PointerEventArgs e)
        => PseudoClasses.Set(ClassOverDetail, true);

    private void OnDetailPointerExited(object? sender, PointerEventArgs e)
        => PseudoClasses.Set(ClassOverDetail, false);

    public static readonly StyledProperty<object?> IconProperty =
        AvaloniaProperty.Register<Cell, object?>(nameof(Icon));

    /// <summary>行首图标；直接给 Geometry 会用 PathIcon 呈现。</summary>
    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> IconTemplateProperty =
        AvaloniaProperty.Register<Cell, IDataTemplate?>(nameof(IconTemplate));

    public IDataTemplate? IconTemplate
    {
        get => GetValue(IconTemplateProperty);
        set => SetValue(IconTemplateProperty, value);
    }

    public static readonly StyledProperty<object?> DescriptionProperty =
        AvaloniaProperty.Register<Cell, object?>(nameof(Description));

    /// <summary>标题下方的说明文字（弱化色）。</summary>
    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> DescriptionTemplateProperty =
        AvaloniaProperty.Register<Cell, IDataTemplate?>(nameof(DescriptionTemplate));

    public IDataTemplate? DescriptionTemplate
    {
        get => GetValue(DescriptionTemplateProperty);
        set => SetValue(DescriptionTemplateProperty, value);
    }

    public static readonly StyledProperty<object?> NoteProperty =
        AvaloniaProperty.Register<Cell, object?>(nameof(Note));

    /// <summary>右侧当前值文本（如“未设置”“Shift”），位于操作区之前。</summary>
    public object? Note
    {
        get => GetValue(NoteProperty);
        set => SetValue(NoteProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> NoteTemplateProperty =
        AvaloniaProperty.Register<Cell, IDataTemplate?>(nameof(NoteTemplate));

    public IDataTemplate? NoteTemplate
    {
        get => GetValue(NoteTemplateProperty);
        set => SetValue(NoteTemplateProperty, value);
    }

    public static readonly StyledProperty<object?> RightContentProperty =
        AvaloniaProperty.Register<Cell, object?>(nameof(RightContent));

    /// <summary>右侧操作区：ToggleSwitch、RadioButton、NumericUpDown 等放这里。</summary>
    public object? RightContent
    {
        get => GetValue(RightContentProperty);
        set => SetValue(RightContentProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> RightContentTemplateProperty =
        AvaloniaProperty.Register<Cell, IDataTemplate?>(nameof(RightContentTemplate));

    public IDataTemplate? RightContentTemplate
    {
        get => GetValue(RightContentTemplateProperty);
        set => SetValue(RightContentTemplateProperty, value);
    }

    public static readonly StyledProperty<object?> DetailProperty =
        AvaloniaProperty.Register<Cell, object?>(nameof(Detail));

    /// <summary>内嵌的详细设置；非空时行尾出现箭头，点击整行展开/收起。</summary>
    public object? Detail
    {
        get => GetValue(DetailProperty);
        set => SetValue(DetailProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> DetailTemplateProperty =
        AvaloniaProperty.Register<Cell, IDataTemplate?>(nameof(DetailTemplate));

    public IDataTemplate? DetailTemplate
    {
        get => GetValue(DetailTemplateProperty);
        set => SetValue(DetailTemplateProperty, value);
    }

    public static readonly StyledProperty<bool> ShowChevronProperty =
        AvaloniaProperty.Register<Cell, bool>(nameof(ShowChevron));

    /// <summary>强制显示行尾箭头（用于绑定 Command 的跳转行；Detail 行自动显示）。</summary>
    public bool ShowChevron
    {
        get => GetValue(ShowChevronProperty);
        set => SetValue(ShowChevronProperty, value);
    }

    public static readonly StyledProperty<bool> IsExpandedProperty =
        AvaloniaProperty.Register<Cell, bool>(nameof(IsExpanded), defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Detail 是否展开（可双向绑定）。</summary>
    public bool IsExpanded
    {
        get => GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == DetailProperty)
        {
            if (change.NewValue is null) IsExpanded = false;
            PseudoClasses.Set(ClassHasDetail, change.NewValue is not null);
        }
        else if (change.Property == IsExpandedProperty)
        {
            PseudoClasses.Set(ClassExpanded, IsExpanded);
            // 收起后详情不再可悬停；残留的 :over-detail 会让下次悬停行首时无高亮。
            if (!IsExpanded) PseudoClasses.Set(ClassOverDetail, false);
        }
    }
}
