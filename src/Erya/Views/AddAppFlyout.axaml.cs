using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Erya.ViewModels.Settings;

namespace Erya.Views;

/// <summary>添加应用弹层内容（挂在「＋ 添加应用」按钮的 Flyout 上）：聚焦搜索框、回车=添加手输标识。</summary>
public partial class AddAppFlyout : UserControl
{
    public AddAppFlyout() => InitializeComponent();

    /// <summary>Flyout 每次打开都会重新挂树——顺带把焦点放进搜索框。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => SearchBox.Focus(), DispatcherPriority.Input);
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is AddAppPickerViewModel vm && vm.AddCustomCommand.CanExecute(null))
        {
            vm.AddCustomCommand.Execute(null);
            e.Handled = true;
        }
    }
}
