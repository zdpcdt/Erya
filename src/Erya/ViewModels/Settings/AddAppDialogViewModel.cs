using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Erya.ViewModels.Settings;

/// <summary>
/// 添加应用对话框（窗口级）：常用应用预设 / 正在运行的应用 / 手填标识三选一，
/// 即时校验（非空、无空白、不与现有规则重复）。确定后由页面 VM 调
/// <see cref="AppOptionsFieldViewModel.AddApp"/> 落进字段（此处再校验一次兜底）。
/// </summary>
public sealed partial class AddAppDialogViewModel : ObservableObject
{
    private readonly HashSet<string> _existingIds;

    public AddAppDialogViewModel(IReadOnlyCollection<string> existingIds,
        IReadOnlyList<AppPresetItem>? presets = null)
    {
        _existingIds = new HashSet<string>(existingIds.Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        // 预设按平台给出（Linux 无外观页、无预设）；可注入供测试在任何平台跑。
        Presets = presets ?? AppBehaviorPresets.ForCurrentOs();
    }

    public IReadOnlyList<AppPresetItem> Presets { get; }

    /// <summary>「正在运行的应用」区仅 Windows：进程 exe 名才对得上 app_options 的键。</summary>
    public bool ShowRunningApps => OperatingSystem.IsWindows();

    public IReadOnlyList<string> RunningApps { get; } = AppBehaviorPresets.RunningWindowsApps();

    [ObservableProperty]
    private string _appId = "";

    /// <summary>添加后立即开启「默认英文」——加应用最常见的意图。</summary>
    [ObservableProperty]
    private bool _startAscii = true;

    public string? ErrorText
    {
        get
        {
            var id = AppId.Trim();
            if (id.Length == 0) return null; // 空是常态，不催
            if (id.Any(char.IsWhiteSpace)) return "标识不能包含空白字符。";
            if (_existingIds.Contains(id)) return $"已有 {id} 的规则。";
            return null;
        }
    }

    /// <summary>确定可用：标识非空且通过校验。Ursa 对话框按钮不禁用，页面按此过滤。</summary>
    public bool IsValid => AppId.Trim().Length > 0 && ErrorText is null;

    public string ConfirmId => AppId.Trim();

    partial void OnAppIdChanged(string value)
    {
        OnPropertyChanged(nameof(ErrorText));
        OnPropertyChanged(nameof(IsValid));
    }

    [RelayCommand]
    private void Pick(AppPresetItem? preset)
    {
        if (preset is not null) AppId = preset.Id;
    }

    [RelayCommand]
    private void PickRunning(string? id)
    {
        if (!string.IsNullOrEmpty(id)) AppId = id;
    }
}
