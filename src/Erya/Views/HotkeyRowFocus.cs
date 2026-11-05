using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya.ViewModels.Settings;
using Ursa.Controls;

namespace Erya.Views;

/// <summary>
/// ➕ 新增行的自动聚焦：FocusOnAttach 绑 true 且控件进入视觉树后聚焦一次，
/// 随即把行 VM 的 IsFresh 复位——聚焦是视图事务，不在 VM 状态上长期停留。
/// Detail 刚展开时布局未稳定，聚焦推迟到 Loaded 优先级执行。
/// </summary>
public static class HotkeyRowFocus
{
    public static readonly AttachedProperty<bool> FocusOnAttachProperty =
        AvaloniaProperty.RegisterAttached<KeyGestureInput, KeyGestureInput, bool>("FocusOnAttach");

    static HotkeyRowFocus()
    {
        FocusOnAttachProperty.Changed.AddClassHandler<KeyGestureInput, bool>(OnFocusOnAttachChanged);
    }

    public static bool GetFocusOnAttach(KeyGestureInput element)
        => element.GetValue(FocusOnAttachProperty);

    public static void SetFocusOnAttach(KeyGestureInput element, bool value)
        => element.SetValue(FocusOnAttachProperty, value);

    private static void OnFocusOnAttachChanged(KeyGestureInput input, AvaloniaPropertyChangedEventArgs<bool> e)
    {
        if (!e.NewValue.Value) return;
        if (!input.GetVisualAncestors().Any())
        {
            input.AttachedToVisualTree -= OnAttached;
            input.AttachedToVisualTree += OnAttached;
            return;
        }
        TryFocus(input);
    }

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs _)
    {
        if (sender is not KeyGestureInput input) return;
        input.AttachedToVisualTree -= OnAttached;
        TryFocus(input);
    }

    private static void TryFocus(KeyGestureInput input)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (!input.GetVisualAncestors().Any()) return;
            input.Focus();
            if (input.DataContext is HotkeyRowViewModel { IsFresh: true } row)
                row.IsFresh = false;
        }, DispatcherPriority.Loaded);
    }
}
