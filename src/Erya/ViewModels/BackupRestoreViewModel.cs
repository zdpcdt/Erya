using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
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
    [NotifyPropertyChangedFor(nameof(HasRestoreSource), nameof(CanRestore), nameof(RestoreNote))]
    private string _restoreSourcePath = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRestore), nameof(CanBackup),
        nameof(BackupButtonText), nameof(RestoreButtonText))]
    private bool _isBusy;

    public bool HasUserDir => UserDir.Length > 0;

    public bool HasRestoreSource => RestoreSourcePath.Length > 0;

    public bool CanRestore => HasRestoreSource && !IsBusy;

    public bool CanBackup => HasUserDir && !IsBusy;

    /// <summary>备份按钮文案：busy 时按钮自己报进度，页面不再放状态条。</summary>
    public string BackupButtonText => IsBusy ? "备份中…" : "选择位置并备份…";

    public string RestoreButtonText => IsBusy ? "恢复中…" : "从备份恢复";

    /// <summary>恢复行右侧当前值：已选备份文件名（截断），未选时提示占位。</summary>
    public string RestoreNote => HasRestoreSource ? Path.GetFileName(RestoreSourcePath) : "未选择";

    /// <summary>Where the pickers start browsing — user documents.</summary>
    public string DefaultStartFolder => Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

    [RelayCommand]
    private void Load()
    {
        if (_distribution is null)
        {
            UserDir = "";
            UserDirExists = false;
            UserDirSummary = "";
            return;
        }

        UserDir = _distribution.UserDir;
        UserDirExists = Directory.Exists(UserDir);
        UserDirSummary = !UserDirExists
            ? "目录尚不存在，此时只能恢复备份"
            : ScanUserDir(UserDir) is { } stats
                ? $"{stats.Files} 个文件，约 {FormatBytes(stats.Bytes)}"
                : "无法读取目录内容";
        BackupFileName = $"RimeBackup_{DateTime.Now:yyyyMMdd-HHmmss}.zip";
    }

    [RelayCommand]
    private void OpenUserDir()
    {
        if (!UserDirExists)
        {
            AppNotifications.Toast("用户目录尚不存在，无法打开", NotificationType.Warning);
            return;
        }

        AppShell.OpenFolder(UserDir);
    }

    /// <summary>View 层的文件选择器选定保存位置后把路径传进来。</summary>
    [RelayCommand]
    private async Task BackupAsync(string? target)
    {
        if (IsBusy || !HasUserDir || string.IsNullOrEmpty(target)) return;

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
            AppNotifications.Toast($"备份失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>View 层的文件选择器选定备份文件后把路径传进来。</summary>
    [RelayCommand]
    private void PickRestoreSource(string? path)
    {
        if (string.IsNullOrEmpty(path)) return; // picker cancelled
        if (!string.Equals(Path.GetExtension(path), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            AppNotifications.Toast("请选择 .zip 格式的备份文件", NotificationType.Warning);
            return;
        }

        RestoreSourcePath = path;
    }

    [RelayCommand]
    private async Task RestoreAsync()
    {
        if (!CanRestore) return;

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
                // The restore itself already succeeded — a deploy problem is
                // reported on its own instead of reading as a restore failure.
                try
                {
                    await _distribution.DeployAsync();
                    AppNotifications.Toast("重新部署完成，恢复的配置已生效", NotificationType.Success);
                }
                catch (Exception ex)
                {
                    AppNotifications.Notify("已恢复，但重新部署失败",
                        $"文件已写回用户目录。{ex.Message} 稍后可按 Ctrl+R 重试。",
                        NotificationType.Warning);
                }
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
            AppNotifications.Toast($"恢复失败：{ex.Message}", NotificationType.Error);
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
