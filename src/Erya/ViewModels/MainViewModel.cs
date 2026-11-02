using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;
using Erya.Core.Distributions.Weasel;
using Erya.Localization;
using Erya.Services;

namespace Erya.ViewModels;

public partial class MainViewModel : ObservableObject
{
    /// <summary>How long the post-install poll keeps scanning for Weasel.</summary>
    private const int SetupPollAttempts = 40;

    private readonly DistributionManager _distributions;
    private readonly AppPreferences _preferences;
    private readonly IWeaselSetup? _weaselSetup;
    private readonly Func<string, bool> _startInstaller;
    private readonly TimeSpan _setupPollInterval;
    private bool _initialized;

    /// <summary>
    /// Every RIME frontend found on this machine. Linux machines can carry
    /// fcitx5-rime and ibus-rime side by side; elsewhere this holds one entry.
    /// Reassigned by re-detection while the app runs.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMultipleDistributions))]
    private IReadOnlyList<IRimeDistribution> _availableDistributions = [];

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

    /// <summary>Offer the in-app Weasel install where it can run (Windows).
    /// The OS call itself lives in the composition root, not in this VM.</summary>
    public bool SupportsInAppSetup { get; }

    [ObservableProperty]
    private string _installDirDisplay = "";

    [ObservableProperty]
    private string _userDir = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(IsDeployerMissing), nameof(IsMissing))]
    private bool _canDeploy;

    /// <summary>下载直装流程进行中：驱动进度条与按钮禁用态。</summary>
    [ObservableProperty]
    private bool _isDownloadingInstaller;

    /// <summary>下载进度 0–100；仅在 <see cref="IsDownloadingInstaller"/> 时显示。</summary>
    [ObservableProperty]
    private double _setupProgress;

    /// <summary>空态引导卡下方的状态行；空串隐藏。</summary>
    [ObservableProperty]
    private string _setupStatus = "";

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

    public MainViewModel(DistributionManager distributions, AppPreferences? preferences = null,
        IWeaselSetup? weaselSetup = null, bool supportsInAppSetup = false,
        Func<string, bool>? installerLauncher = null, TimeSpan? setupPollInterval = null)
    {
        _distributions = distributions;
        _preferences = preferences ?? AppPreferences.Load();
        // The OS call lives in the composition root (App.axaml.cs); tests
        // pass the flag explicitly, so the default here is just "off".
        SupportsInAppSetup = supportsInAppSetup;
        _weaselSetup = weaselSetup ?? (SupportsInAppSetup ? new WeaselSetup() : null);
        _startInstaller = installerLauncher ?? StartInstallerWithShell;
        _setupPollInterval = setupPollInterval ?? TimeSpan.FromSeconds(3);

        ApplyAutoDetection();
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

    /// <summary>
    /// Re-runs every locator and re-picks the effective frontend. When the
    /// outcome is unchanged (same id) only the info fields refresh — a full
    /// <see cref="Distribution"/> swap would rebuild every page and drop
    /// unsaved edits. Auto-detection never touches the stored preference.
    /// </summary>
    private void ApplyAutoDetection()
    {
        AvailableDistributions = _distributions.LocateAll();
        var preferred = AvailableDistributions.FirstOrDefault(d => d.Id == _preferences.DistributionId)
                        ?? AvailableDistributions.FirstOrDefault();
        if (preferred?.Id == Distribution?.Id)
        {
            RefreshFromDistribution();
            return;
        }

        _initialized = false; // keep OnDistributionChanged from persisting
        try { Distribution = preferred; }
        finally { _initialized = true; }
    }

    /// <summary>Rescans the machine for RIME distributions without a restart.</summary>
    [RelayCommand]
    private void Redetect()
    {
        ApplyAutoDetection();
        var m = LanguageManager.Instance;
        if (Distribution is null)
            AppNotifications.Toast(Loc.Get(m.overview_redetectNoneToast), NotificationType.Information);
        else
            AppNotifications.Toast(Loc.Format(m.overview_redetectFoundToast, Distribution.DisplayName),
                NotificationType.Success);
    }

    /// <summary>Opens the official RIME download page in the system browser.</summary>
    [RelayCommand]
    private void OpenDownloadPage() => AppShell.OpenUrl("https://rime.im/download");

    /// <summary>
    /// One-click Weasel setup: resolve the latest release, stream the
    /// installer down with progress, hand it to the OS shell (UAC prompt is
    /// the installer's own), then poll detection until the fresh install
    /// lights the page up — no restart, no manual re-detect needed.
    /// </summary>
    [RelayCommand]
    private async Task DownloadAndInstallAsync()
    {
        if (!SupportsInAppSetup || _weaselSetup is null || IsDownloadingInstaller) return;

        var m = LanguageManager.Instance;
        IsDownloadingInstaller = true;
        SetupProgress = 0;
        SetupStatus = Loc.Get(m.overview_setupPreparing);
        try
        {
            var release = await _weaselSetup.GetLatestReleaseAsync();
            var progress = new Progress<double>(p =>
            {
                SetupProgress = p;
                SetupStatus = Loc.Format(m.overview_setupDownloading, p);
            });
            var installerPath = await _weaselSetup.DownloadInstallerAsync(release, progress);

            SetupStatus = Loc.Get(m.overview_setupLaunching);
            if (!_startInstaller(installerPath))
                throw new InvalidOperationException(Loc.Get(m.overview_setupLaunchFailed));

            // The WiX bundle elevates itself, so the launched handle is not a
            // reliable completion signal — poll detection instead until the
            // freshly installed Weasel appears (installer wizard takes a while;
            // the user may also cancel it, hence the bounded window).
            SetupStatus = Loc.Get(m.overview_setupWaiting);
            for (var attempt = 0; attempt < SetupPollAttempts; attempt++)
            {
                await Task.Delay(_setupPollInterval);
                if (_distributions.Locate() is { } found)
                {
                    ApplyAutoDetection();
                    AppNotifications.Toast(
                        Loc.Format(m.overview_redetectFoundToast, found.DisplayName), NotificationType.Success);
                    SetupStatus = "";
                    return;
                }
            }

            SetupStatus = Loc.Get(m.overview_setupPollTimeout);
            AppNotifications.Toast(Loc.Get(m.overview_setupPollTimeout), NotificationType.Information);
        }
        catch (Exception ex)
        {
            SetupStatus = "";
            AppNotifications.Toast(Loc.Format(m.overview_setupFailedToast, ex.Message), NotificationType.Error);
        }
        finally
        {
            IsDownloadingInstaller = false;
        }
    }

    private static bool StartInstallerWithShell(string installerPath)
    {
        try
        {
            return Process.Start(new ProcessStartInfo(installerPath) { UseShellExecute = true }) is not null;
        }
        catch (Exception)
        {
            return false;
        }
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
