using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;
using Erya.Localization;
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
    [NotifyPropertyChangedFor(nameof(HeroTitle), nameof(HeroVersion), nameof(HeroHint),
        nameof(IsReady), nameof(IsDeployerMissing), nameof(IsMissing))]
    private IRimeDistribution? _distribution;

    /// <summary>Raised after <see cref="Distribution"/> changed at runtime.</summary>
    public event Action? DistributionChanged;

    public bool HasMultipleDistributions => AvailableDistributions.Count > 1;

    [ObservableProperty]
    private string _installDirDisplay = "";

    [ObservableProperty]
    private string _userDir = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsDeployerMissing), nameof(IsMissing))]
    private bool _canDeploy;

    /// <summary>英雄卡右上的状态徽标；三态互斥，颜色由视图按态取。</summary>
    public bool IsReady => Distribution is not null && CanDeploy;
    public bool IsDeployerMissing => Distribution is not null && !CanDeploy;
    public bool IsMissing => Distribution is null;

    /// <summary>英雄卡主标题：发行版名；未检测到时是状态文案。</summary>
    public string HeroTitle => Distribution?.DisplayName ?? Loc.Get(LanguageManager.Instance.overview_distMissing);

    /// <summary>英雄卡副行的版本号；缺失时为空（视图隐藏）。</summary>
    public string HeroVersion => Distribution?.Version is { } v ? $"v{v}" : "";

    /// <summary>英雄卡副行的提示文字：就绪时为空，异常时是原因或安装指引。</summary>
    public string HeroHint => Distribution switch
    {
        null => InstallHints.ForCurrentOs,
        { CanDeploy: false } d => InstallHints.DeployerMissingHint(d),
        _ => "",
    };

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

    /// <summary>换语言后由壳调用：长驻 VM 的计算属性重新取值（页面 VM 随重建自然更新）。</summary>
    public void RefreshLocalizedTexts()
    {
        OnPropertyChanged(nameof(HeroTitle));
        OnPropertyChanged(nameof(HeroVersion));
        OnPropertyChanged(nameof(HeroHint));
    }

    private void RefreshFromDistribution()
    {
        if (Distribution is null)
        {
            InstallDirDisplay = "";
            UserDir = "";
            CanDeploy = false;
            return;
        }

        InstallDirDisplay = Distribution.InstallDir ?? "";
        UserDir = Distribution.UserDir;
        CanDeploy = Distribution.CanDeploy;
    }

    [RelayCommand]
    private void OpenUserDir()
    {
        if (Distribution is null) return;

        var dir = Distribution.UserDir;
        if (!Directory.Exists(dir))
        {
            AppNotifications.Toast(
                Loc.Format(LanguageManager.Instance.overview_userDirMissingToast, dir), NotificationType.Warning);
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
            AppNotifications.Toast(Loc.Get(LanguageManager.Instance.overview_deployDoneToast), NotificationType.Success);
        }
        catch (Exception ex)
        {
            AppNotifications.Toast(
                Loc.Format(LanguageManager.Instance.overview_deployFailedToast, ex.Message), NotificationType.Error);
        }
    }
}
