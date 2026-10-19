using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;
using Erya.Services;

namespace Erya.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DistributionManager _distributions;

    public IRimeDistribution? Distribution { get; private set; }

    [ObservableProperty]
    private string _detectionTitle = "正在检测 RIME 发行版…";

    [ObservableProperty]
    private string _installDirDisplay = "";

    [ObservableProperty]
    private string _userDir = "";

    [ObservableProperty]
    private bool _canDeploy;

    /// <summary>Persistent detection state — banner material, not toast material.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasStatus))]
    private string _statusMessage = "";

    [ObservableProperty]
    private NotificationType _statusType = NotificationType.Information;

    public bool HasStatus => !string.IsNullOrEmpty(StatusMessage);

    public MainViewModel(DistributionManager distributions)
    {
        _distributions = distributions;
        Detect();
    }

    private void Detect()
    {
        Distribution = _distributions.Locate();
        if (Distribution is null)
        {
            DetectionTitle = "未检测到 RIME 发行版";
            StatusType = NotificationType.Warning;
            StatusMessage = InstallHints.ForCurrentOs;
            return;
        }

        DetectionTitle = Distribution.Version is { } version
            ? $"{Distribution.DisplayName} · v{version}"
            : Distribution.DisplayName;
        InstallDirDisplay = Distribution.InstallDir ?? "";
        UserDir = Distribution.UserDir;
        CanDeploy = Distribution.CanDeploy;
        if (CanDeploy)
        {
            StatusType = NotificationType.Success;
            StatusMessage = "就绪。";
        }
        else
        {
            StatusType = NotificationType.Warning;
            StatusMessage = InstallHints.DeployerMissingHint(null);
        }
    }

    [RelayCommand]
    private void OpenUserDir()
    {
        if (Distribution is null) return;

        var dir = Distribution.UserDir;
        if (!Directory.Exists(dir))
        {
            AppNotifications.Toast($"用户目录尚不存在：{dir}（输入法首次部署后会自动创建）", NotificationType.Warning);
            return;
        }

        AppShell.OpenFolder(dir);
    }

    [RelayCommand]
    private async Task DeployAsync()
    {
        if (Distribution is null) return;

        try
        {
            await Distribution.DeployAsync();
            AppNotifications.Toast("已触发重新部署，修改过的 YAML 配置将重新生效。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            AppNotifications.Toast($"部署失败：{ex.Message}", NotificationType.Error);
        }
    }
}
