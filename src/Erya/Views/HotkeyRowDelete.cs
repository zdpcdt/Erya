using System.Linq;
using System.Windows.Input;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using Ursa.Controls;

namespace Erya.Views;

/// <summary>
/// 把 hotkey 行内 KeyGestureInput 的清除按钮（模板 PART_ClearButton）点击转译为
/// 删行命令，与键盘清空区分开：Escape / 只按修饰键松开只会经绑定把行值清空
/// （行保留，等重录），唯有鼠标点 × 才删除整行 card。按钮 Focusable=False，
/// 不存在键盘触发路径，Click 即鼠标语义。
/// </summary>
public static class HotkeyRowDelete
{
    public static readonly AttachedProperty<ICommand?> DeleteCommandProperty =
        AvaloniaProperty.RegisterAttached<KeyGestureInput, KeyGestureInput, ICommand?>("DeleteCommand");

    static HotkeyRowDelete()
    {
        DeleteCommandProperty.Changed.AddClassHandler<KeyGestureInput, ICommand?>(OnDeleteCommandChanged);
    }

    public static ICommand? GetDeleteCommand(KeyGestureInput element)
        => element.GetValue(DeleteCommandProperty);

    public static void SetDeleteCommand(KeyGestureInput element, ICommand? value)
        => element.SetValue(DeleteCommandProperty, value);

    private static void OnDeleteCommandChanged(KeyGestureInput element, AvaloniaPropertyChangedEventArgs<ICommand?> _)
    {
        element.TemplateApplied -= OnTemplateApplied;
        element.TemplateApplied += OnTemplateApplied;
        // 附加属性可能晚于模板应用设置（绑定顺序不保证），补挂一次现成按钮。
        Hook(element);
    }

    private static void OnTemplateApplied(object? sender, TemplateAppliedEventArgs _)
    {
        if (sender is KeyGestureInput input) Hook(input);
    }

    private static void Hook(KeyGestureInput input)
    {
        // 主题里的清除按钮；PART_ClearButton 是 Ursa Semi 主题的实现细节，
        // 找不到就静默退化为「只有键盘清空」，不崩。
        foreach (var clear in input.GetVisualDescendants().OfType<Button>()
                     .Where(b => b.Name == "PART_ClearButton").ToList())
        {
            clear.Click -= OnClearClick;
            clear.Click += OnClearClick;
        }
    }

    private static void OnClearClick(object? sender, RoutedEventArgs _)
    {
        if (sender is not Button button) return;
        var input = button.GetVisualAncestors().OfType<KeyGestureInput>().FirstOrDefault();
        if (input?.DataContext is not { } row) return;
        var command = GetDeleteCommand(input);
        if (command?.CanExecute(row) == true) command.Execute(row);
    }
}
