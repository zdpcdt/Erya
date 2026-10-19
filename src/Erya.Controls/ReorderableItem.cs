using Avalonia.Controls;
using Avalonia.Controls.Metadata;

namespace Erya.Controls;

/// <summary>
/// <see cref="ReorderableItemsControl"/> 的行容器：ContentControl，拖拽期间由
/// 控件维护 <c>:dragging</c> 伪类（卡片背景 + 投影浮起，见 ReorderTheme.axaml）。
/// </summary>
[PseudoClasses(":dragging", ":pressed")]
public class ReorderableItem : ContentControl
{
    internal void SetDragging(bool value) => PseudoClasses.Set(":dragging", value);

    /// <summary>
    /// 按住把手时的按压反馈。伪类只能由控件自己设置（Avalonia 12 的 Classes
    /// 对外部写入伪类直接抛异常），故经此方法由 ReorderableItemsControl 驱动。
    /// </summary>
    internal void SetPressed(bool value) => PseudoClasses.Set(":pressed", value);
}
