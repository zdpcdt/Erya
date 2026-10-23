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
using Erya.Localization;
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
    public string BackupButtonText => IsBusy
        ? Loc.Get(LanguageManager.Instance.backup_backupBusy)
        : Loc.Get(LanguageManager.Instance.backup_backupAction);

    public string RestoreButtonText => IsBusy
        ? Loc.Get(LanguageManager.Instance.backup_restoreBusy)
        : Loc.Get(LanguageManager.Instance.backup_restoreLabel);

    /// <summary>恢复行右侧当前值：已选备份文件名（截断），未选时提示占位。</summary>
    public string RestoreNote => HasRestoreSource
        ? Path.GetFileName(RestoreSourcePath)
        : Loc.Get(LanguageManager.Instance.backup_noSelection);

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
            ? Loc.Get(LanguageManager.Instance.backup_dirMissingSummary)
            : ScanUserDir(UserDir) is { } stats
                ? Loc.Format(LanguageManager.Instance.backup_dirSummary, stats.Files, FormatBytes(stats.Bytes))
                : Loc.Get(LanguageManager.Instance.backup_dirUnreadable);
        BackupFileName = $"RimeBackup_{DateTime.Now:yyyyMMdd-HHmmss}.zip";
    }

    [RelayCommand]
    private void OpenUserDir()
    {
        if (!UserDirExists)
        {
            AppNotifications.Toast(Loc.Get(LanguageManager.Instance.backup_openNoDirToast), NotificationType.Warning);
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
                AppNotifications.Notify(Loc.Get(LanguageManager.Instance.backup_skippedPackTitle),
                    string.Join('\n', report.SkippedFiles), NotificationType.Warning);
            AppNotifications.Toast(
                Loc.Format(LanguageManager.Instance.backup_backupDoneToast, report.FileCount, Path.GetFileName(target)),
                NotificationType.Success);
            BackupFileName = $"RimeBackup_{DateTime.Now:yyyyMMdd-HHmmss}.zip";
        }
        catch (Exception ex)
        {
            AppNotifications.Toast(Loc.Format(LanguageManager.Instance.backup_backupFailedToast, ex.Message),
                NotificationType.Error);
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
            AppNotifications.Toast(Loc.Get(LanguageManager.Instance.backup_invalidZipToast), NotificationType.Warning);
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
                Title = Loc.Get(LanguageManager.Instance.backup_confirmTitle),
                Mode = DialogMode.Question,
                Buttons = DialogButton.OKCancel,
            });
        if (result != DialogResult.OK) return;

        IsBusy = true;
        try
        {
            var report = await Task.Run(() => RimeBackup.Restore(RestoreSourcePath, UserDir));
            AppNotifications.Notify(Loc.Get(LanguageManager.Instance.backup_restoreDoneTitle),
                Loc.Format(LanguageManager.Instance.backup_restoreDoneDetail, report.FileCount)
                + (report.SafetyBackupPath is { } safety
                    ? Loc.Format(LanguageManager.Instance.backup_safetyBackupTail, safety)
                    : ""),
                NotificationType.Success);
            if (report.HasSkippedFiles)
                AppNotifications.Notify(Loc.Get(LanguageManager.Instance.backup_skippedRestoreTitle),
                    string.Join('\n', report.SkippedFiles), NotificationType.Warning);

            if (_distribution is { CanDeploy: true })
            {
                // The restore itself already succeeded — a deploy problem is
                // reported on its own instead of reading as a restore failure.
                try
                {
                    await _distribution.DeployAsync();
                    AppNotifications.Toast(Loc.Get(LanguageManager.Instance.backup_redeployDoneToast),
                        NotificationType.Success);
                }
                catch (Exception ex)
                {
                    AppNotifications.Notify(Loc.Get(LanguageManager.Instance.backup_redeployFailedTitle),
                        Loc.Format(LanguageManager.Instance.backup_redeployFailedDetail, ex.Message),
                        NotificationType.Warning);
                }
            }
            else
            {
                AppNotifications.Notify(Loc.Get(LanguageManager.Instance.backup_needRedeployTitle),
                    Loc.Get(LanguageManager.Instance.backup_needRedeployDetail), NotificationType.Warning);
            }

            RestoreSourcePath = "";
            Load();
        }
        catch (Exception ex)
        {
            AppNotifications.Toast(Loc.Format(LanguageManager.Instance.backup_restoreFailedToast, ex.Message),
                NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private static Control BuildConfirmContent(string fileName, string userDir)
    {
        var m = LanguageManager.Instance;
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock { Text = Loc.Format(m.backup_confirmBackupFile, fileName), FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = Loc.Format(m.backup_confirmTarget, userDir), TextWrapping = TextWrapping.Wrap, Opacity = 0.72 },
                new TextBlock
                {
                    Text = Loc.Get(m.backup_confirmWarning),
                    TextWrapping = TextWrapping.Wrap,
                },
            }
        };
    }

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
