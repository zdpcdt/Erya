using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;

namespace Erya.Controls;

/// <summary>
/// Cell 的卡片容器：Header/Description 之下是一张 Semi 风格圆角卡片，相邻行之间
/// 自动画分隔线（最后一行除外）。卡片圆角由 <see cref="CornerRadius"/> 决定，
/// 行的悬停/按压背景会按首行/末行方位继承该圆角（见 CellStyles.axaml）。
/// 子项既可以直接写 Cell，也可以是任意模型——没有 ItemTemplate 时模型会自动
/// 包进 Cell 并作为标题显示。
/// </summary>
public class CellGroup : HeaderedItemsControl
{
    public static readonly StyledProperty<CornerRadius> CornerRadiusProperty =
        AvaloniaProperty.Register<CellGroup, CornerRadius>(nameof(CornerRadius));

    /// <summary>卡片圆角；行内通过 $parent 绑定按方位遮罩继承（CornerRadiusMixerConverter）。</summary>
    public CornerRadius CornerRadius
    {
        get => GetValue(CornerRadiusProperty);
        set => SetValue(CornerRadiusProperty, value);
    }

    public static readonly StyledProperty<object?> DescriptionProperty =
        AvaloniaProperty.Register<CellGroup, object?>(nameof(Description));

    /// <summary>分组说明，显示在 Header 与卡片之间（弱化色）。</summary>
    public object? Description
    {
        get => GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public static readonly StyledProperty<IDataTemplate?> DescriptionTemplateProperty =
        AvaloniaProperty.Register<CellGroup, IDataTemplate?>(nameof(DescriptionTemplate));

    public IDataTemplate? DescriptionTemplate
    {
        get => GetValue(DescriptionTemplateProperty);
        set => SetValue(DescriptionTemplateProperty, value);
    }

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
    {
        recycleKey = null;
        return item is not Control;
    }

    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
    {
        return ItemTemplate is null ? new Cell() : new ContentPresenter();
    }

    protected override void PrepareContainerForItemOverride(Control container, object? item, int index)
    {
        base.PrepareContainerForItemOverride(container, item, index);
        if (container is ContentPresenter presenter)
        {
            presenter.Content = item;
            presenter.ContentTemplate = ItemTemplate;
        }
        else if (container is Cell cell && !cell.IsSet(ContentControl.ContentTemplateProperty))
        {
            cell.ContentTemplate = ItemTemplate;
        }
    }
}
