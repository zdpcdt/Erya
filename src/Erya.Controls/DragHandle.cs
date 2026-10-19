using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;

namespace Erya.Controls;

/// <summary>
/// 拖拽把手：放入 <see cref="ReorderableItemsControl"/> 的行模板即成为该行的
/// 拖拽起点（构造时给自己标记 IsDragHandle）。Data 直接给图标几何，如
/// <c>Data="{DynamicResource SemiIconHandle}"</c>；默认样式（ReorderTheme.axaml）
/// 提供弱化图标、悬停加深与 SizeAll 光标。
/// </summary>
public class DragHandle : TemplatedControl
{
    public static readonly StyledProperty<Geometry?> DataProperty =
        AvaloniaProperty.Register<DragHandle, Geometry?>(nameof(Data));

    /// <summary>把手图标几何（PathIcon Data）。</summary>
    public Geometry? Data
    {
        get => GetValue(DataProperty);
        set => SetValue(DataProperty, value);
    }

    /// <summary>按压反馈；伪类只能由控件自己设置，经此由 ReorderableItemsControl 驱动。</summary>
    internal void SetPressed(bool value) => PseudoClasses.Set(":pressed", value);

    public DragHandle() => ReorderableItemsControl.SetIsDragHandle(this, true);
}
