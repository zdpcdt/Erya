using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;

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
            StatusMessage = "请先安装小狼毫（Windows）：https://rime.im/download 。安装完成后重新打开尔雅。";
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
            StatusMessage = "未找到部署器 WeaselDeployer.exe，无法触发重新部署。";
        }
    }

    [RelayCommand]
    private void OpenUserDir()
    {
        if (Distribution is null) return;

        var dir = Distribution.UserDir;
        if (!Directory.Exists(dir))
        {
            StatusType = NotificationType.Warning;
            StatusMessage = $"用户目录尚不存在：{dir}（输入法首次部署后会自动创建）";
            return;
        }

        Process.Start(new ProcessStartInfo(dir) { UseShellExecute = true });
    }

    [RelayCommand]
    private async Task DeployAsync()
    {
        if (Distribution is null) return;

        try
        {
            StatusType = NotificationType.Information;
            StatusMessage = "正在触发重新部署…";
            await Distribution.DeployAsync();
            StatusType = NotificationType.Success;
            StatusMessage = "已触发重新部署，修改过的 YAML 配置将重新生效。";
        }
        catch (Exception ex)
        {
            StatusType = NotificationType.Error;
            StatusMessage = $"部署失败：{ex.Message}";
        }
    }
}
