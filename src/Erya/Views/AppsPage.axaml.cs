using System;
using Avalonia;
using Avalonia.Controls;
using Erya.Controls;
using Erya.ViewModels.Settings;

namespace Erya.Views;

/// <summary>应用行为页：按应用设置输入行为（app_options，exe 名 / Bundle ID）。</summary>
public partial class AppsPage : UserControl
{
    private AppsPageViewModel? _subscribed;

    public AppsPage()
    {
        InitializeComponent();
        // Button 带 Flyout 时点击只开弹层、不执行 Command——弹层数据（PickerEntries，
        // 含最新「已添加」状态）在打开瞬间同步重建；SearchablePicker 的 Text 等随之复位。
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

    /// <summary>弹层点选与手输回车走同一条路：字段添加（默认英文）并收起。</summary>
    private void OnPickerEntryPicked(object? sender, EntryPickedEventArgs e) => AddPicked(e.Entry.Id);

    private void OnPickerCustomEntryAdded(object? sender, CustomEntryAddedEventArgs e) => AddPicked(e.Id);

    private void AddPicked(string appId)
    {
        if (DataContext is AppsPageViewModel vm) vm.AddPicked(appId);
    }

    private void CloseAddFlyout(object? sender, EventArgs e) => AddButton.Flyout?.Hide();
}
