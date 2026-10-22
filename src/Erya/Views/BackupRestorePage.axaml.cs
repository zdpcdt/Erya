using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Erya.ViewModels;

namespace Erya.Views;

public partial class BackupRestorePage : UserControl
{
    public BackupRestorePage()
    {
        InitializeComponent();
    }

    /// <summary>备份按钮：先弹保存位置选择器，选定后把路径交给 VM 执行备份。</summary>
    private async void OnBackupClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BackupRestoreViewModel vm || !vm.CanBackup) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var file = await storage.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "选择备份保存位置",
            SuggestedFileName = vm.BackupFileName,
            SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(vm.DefaultStartFolder),
            DefaultExtension = "zip",
            FileTypeChoices = [BackupFileType],
        });
        if (file?.TryGetLocalPath() is { } path)
            vm.BackupCommand.Execute(path);
    }

    /// <summary>选择备份文件按钮：弹打开选择器，选定后路径存进 VM，恢复按钮随即点亮。</summary>
    private async void OnPickRestoreClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not BackupRestoreViewModel vm || vm.IsBusy) return;
        var storage = TopLevel.GetTopLevel(this)?.StorageProvider;
        if (storage is null) return;

        var files = await storage.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "选择要恢复的备份压缩包",
            SuggestedStartLocation = await storage.TryGetFolderFromPathAsync(vm.DefaultStartFolder),
            FileTypeFilter = [BackupFileType],
            AllowMultiple = false,
        });
        if (files.FirstOrDefault()?.TryGetLocalPath() is { } path)
            vm.PickRestoreSourceCommand.Execute(path);
    }

    private static FilePickerFileType BackupFileType { get; } = new("备份压缩包")
    {
        Patterns = ["*.zip"],
    };
}
