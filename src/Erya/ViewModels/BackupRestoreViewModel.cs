using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Backup;
using Erya.Core.Distributions;
using Erya.Services;
using Ursa.Controls;

namespace Erya.ViewModels;

public partial class BackupRestoreViewModel : ObservableObject
{
    private readonly IRimeDistribution? _distribution;

    public BackupRestoreViewModel(IRimeDistribution? distribution) => _distribution = distribution;

    [ObservableProperty]
    private string _userDir = "";

    [ObservableProperty]
    private bool _userDirExists;

    [ObservableProperty]
    private string _userDirSummary = "";

    /// <summary>File name suggested to the save-file picker; refreshed after each backup.</summary>
    [ObservableProperty]
    private string _backupFileName = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasRestoreSource), nameof(CanRestore))]
    private string _restoreSourcePath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    private string _hint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasUserDir => UserDir.Length > 0;

    public bool HasRestoreSource => RestoreSourcePath.Length > 0;

    public bool CanRestore => HasRestoreSource && !IsBusy;

    public bool HasHint => Hint.Length > 0;

    public bool HasError => ErrorMessage.Length > 0;

    /// <summary>Where the pickers start browsing — user documents.</summary>
    public string DefaultStartFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [RelayCommand]
    private void Load()
    {
        Hint = "";
        ErrorMessage = "";
        if (_distribution is null)
        {
            UserDir = "";
            UserDirExists = false;
            UserDirSummary = "";
            Hint = "未检测到 RIME 发行版：" + InstallHints.ForCurrentOs;
            return;
        }

        UserDir = _distribution.UserDir;
        UserDirExists = Directory.Exists(UserDir);
        UserDirSummary = !UserDirExists
            ? "目录尚不存在（输入法首次部署后会自动创建），此时只能恢复备份。"
            : ScanUserDir(UserDir) is { } stats
                ? $"{stats.Files} 个文件，约 {FormatBytes(stats.Bytes)}"
                : "无法读取目录内容。";
        BackupFileName = $"RimeBackup_{DateTime.Now:yyyyMMdd-HHmmss}.zip";
    }

    [RelayCommand]
    private void OpenUserDir()
    {
        if (!UserDirExists)
        {
            AppNotifications.Toast("用户目录尚不存在，无法打开。", NotificationType.Warning);
            return;
        }

        AppShell.OpenFolder(UserDir);
    }

    /// <summary>PathPicker hands the picked items to Command as IStorageItem list.</summary>
    [RelayCommand]
    private async Task BackupAsync(object? parameter)
    {
        if (IsBusy || !HasUserDir) return;

        var target = SingleLocalPath(parameter);
        if (target is null)
        {
            AppNotifications.Toast("未选择保存位置，已取消备份。", NotificationType.Information);
            return;
        }

        ErrorMessage = "";
        IsBusy = true;
        try
        {
            var report = await Task.Run(() => RimeBackup.Create(UserDir, target));
            if (report.HasSkippedFiles)
                AppNotifications.Notify("部分文件未能打包（可能正被输入法占用）",
                    string.Join('\n', report.SkippedFiles), NotificationType.Warning);
            AppNotifications.Toast($"备份完成：{report.FileCount} 个文件 → {Path.GetFileName(target)}",
                NotificationType.Success);
            BackupFileName = $"RimeBackup_{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"备份失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private void PickRestoreSource(object? parameter)
    {
        var path = SingleLocalPath(parameter);
        if (path is null) return; // picker cancelled
        if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            AppNotifications.Toast("请选择 .zip 格式的备份文件。", NotificationType.Warning);
            return;
        }

        RestoreSourcePath = path;
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (!CanRestore) return;

        ErrorMessage = "";
        var result = await OverlayDialog.ShowStandardAsync(
            BuildConfirmContent(Path.GetFileName(RestoreSourcePath), UserDir), null, "main",
            new OverlayDialogOptions
            {
                Title = "确认恢复备份",
                Mode = DialogMode.Question,
                Buttons = DialogButton.OKCancel,
            });
        if (result != DialogResult.OK) return;

        IsBusy = true;
        try
        {
            var report = await Task.Run(() => RimeBackup.Restore(RestoreSourcePath, UserDir));
            AppNotifications.Notify("恢复完成",
                $"{report.FileCount} 个文件已写回用户目录。"
                + (report.SafetyBackupPath is { } safety ? $"\n恢复前的状态已保存到：{safety}" : ""),
                NotificationType.Success);
            if (report.HasSkippedFiles)
                AppNotifications.Notify("部分文件未能恢复", string.Join('\n', report.SkippedFiles),
                    NotificationType.Warning);

            if (_distribution is { CanDeploy: true })
            {
                await _distribution.DeployAsync();
                AppNotifications.Toast("已触发重新部署，恢复的配置即将生效。", NotificationType.Success);
            }
            else
            {
                AppNotifications.Notify("需要重新部署",
                    "未找到部署器，恢复的配置需手动重新部署后生效。", NotificationType.Warning);
            }

            RestoreSourcePath = "";
            Load();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"恢复失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Control BuildConfirmContent(string fileName, string userDir)
        => new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = $"备份文件：{fileName}", FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = $"恢复目标：{userDir}", TextWrapping = TextWrapping.Wrap, Opacity = 0.72 },
                new TextBlock
                {
                    Text = "同名文件将被覆盖；恢复前会自动在备份所在目录生成安全备份（pre-restore-*.zip），如有问题可再恢复它回滚。",
                    TextWrapping = TextWrapping.Wrap,
                },
            }
        };

    /// <summary>PathPicker passes IReadOnlyList&lt;IStorageItem&gt;; keep the first local path.</summary>
    private static string? SingleLocalPath(object? parameter)
        => (parameter as IEnumerable)?.OfType<IStorageItem>()
            .Select(i => i.TryGetLocalPath())
            .FirstOrDefault(p => !string.IsNullOrEmpty(p));

    private static (int Files, long Bytes)? ScanUserDir(string dir)
    {
        try
        {
            int files = 0;
            long bytes = 0;
            foreach (var file in Directory.EnumerateFiles(dir, "*", new EnumerationOptions
                     {
                         RecurseSubdirectories = true,
                         IgnoreInaccessible = true,
                     }))
            {
                files++;
                try { bytes += new FileInfo(file).Length; }
                catch (IOException) { /* file vanished or locked; count it anyway */ }
            }

            return (files, bytes);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:F1} KB",
        < 1024L * 1024 * 1024 => $"{bytes / 1024.0 / 1024:F1} MB",
        _ => $"{bytes / 1024.0 / 1024 / 1024:F2} GB",
    };
}
