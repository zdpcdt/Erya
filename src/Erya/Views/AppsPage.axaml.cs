using System;
using Avalonia;
using Avalonia.Controls;
using Erya.ViewModels.Settings;

namespace Erya.Views;

/// <summary>应用行为页：按应用设置输入行为（app_options，exe 名 / Bundle ID）。</summary>
public partial class AppsPage : UserControl
{
    private AppsPageViewModel? _subscribed;

    public AppsPage()
    {
        InitializeComponent();
        // Button 带 Flyout 时点击只开弹层、不执行 Command——弹层数据（Picker，
        // 含最新「已添加」状态）在打开瞬间同步重建，DataContext={Binding Picker} 随之更新。
        AddButton.Flyout!.Opened += (_, _) =>
        {
            if (DataContext is AppsPageViewModel vm) vm.OpenPickerCommand.Execute(null);
        };
    }

    /// <summary>弹层里点选/回车添加完成（AddPicked → RequestClosePicker）→ 收起 Flyout。</summary>
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_subscribed is { } old) old.RequestClosePicker -= CloseAddFlyout;
        _subscribed = DataContext as AppsPageViewModel;
        if (_subscribed is { } vm) vm.RequestClosePicker += CloseAddFlyout;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_subscribed is { } old) old.RequestClosePicker -= CloseAddFlyout;
        _subscribed = null;
        base.OnDetachedFromVisualTree(e);
    }

    private void CloseAddFlyout(object? sender, EventArgs e) => AddButton.Flyout?.Hide();
}
