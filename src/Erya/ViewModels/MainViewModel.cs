using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;
using Erya.Services;

namespace Erya.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly AppPreferences _preferences;
    private bool _initialized;

    /// <summary>
    /// Every RIME frontend found on this machine. Linux machines can carry
    /// fcitx5-rime and ibus-rime side by side; elsewhere this holds one entry.
    /// </summary>
    public IReadOnlyList<IRimeDistribution> AvailableDistributions { get; }

    /// <summary>
    /// The frontend Erya is currently managing. Two-way bound to the overview
    /// selector; switching rebuilds every page (see ShellViewModel).
    /// </summary>
    [ObservableProperty]
    private IRimeDistribution? _distribution;

    /// <summary>Raised after <see cref="Distribution"/> changed at runtime.</summary>
    public event Action? DistributionChanged;

    public bool HasMultipleDistributions => AvailableDistributions.Count > 1;

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

    public MainViewModel(DistributionManager distributions, AppPreferences? preferences = null)
    {
        _preferences = preferences ?? AppPreferences.Load();
        AvailableDistributions = distributions.LocateAll();
        // Prefer the frontend picked last time; otherwise locator order decides
        // (fcitx5 ahead of ibus on Linux, per App.axaml.cs composition).
        Distribution = AvailableDistributions.FirstOrDefault(d => d.Id == _preferences.DistributionId)
                       ?? AvailableDistributions.FirstOrDefault();
        _initialized = true;
    }

    partial void OnDistributionChanged(IRimeDistribution? value)
    {
        // Only a real user choice is persisted — auto-detection must not
        // overwrite a preference just because its frontend is gone this run.
        if (value is not null && _initialized && HasMultipleDistributions)
        {
            _preferences.DistributionId = value.Id;
            _preferences.Save();
        }
        RefreshFromDistribution();
        DistributionChanged?.Invoke();
    }

    private void RefreshFromDistribution()
    {
        if (Distribution is null)
        {
            DetectionTitle = "未检测到 RIME 发行版";
            StatusType = NotificationType.Warning;
            StatusMessage = InstallHints.ForCurrentOs;
            InstallDirDisplay = "";
            UserDir = "";
            CanDeploy = false;
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
            StatusMessage = InstallHints.DeployerMissingHint(Distribution);
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
