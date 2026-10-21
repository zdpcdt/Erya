using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;
using Erya.Services;

namespace Erya.ViewModels.Advanced;

/// <summary>One config file target in 高级设置 (default.yaml always; the frontend YAML when it has one).</summary>
public sealed record ConfigTarget(string BaseFile, string Title)
{
    public override string ToString() => Title;
}

/// <summary>
/// 高级设置：以源码方式直接编辑补丁文件（default.custom.yaml 等），
/// AvaloniaEdit 编辑器承载文本；保存前由 RimeConfigStore 做 YAML 校验并留 .bak 备份。
/// </summary>
public sealed partial class AdvancedSettingsViewModel : ObservableObject
{
    private readonly IRimeDistribution? _distribution;
    private readonly Core.Configuration.RimeConfigStore? _store;
    private string _loadedText = "";

    public AdvancedSettingsViewModel(IRimeDistribution? distribution)
    {
        _distribution = distribution;
        _store = distribution?.GetConfigStore();
        foreach (var target in BuildTargets(distribution))
            Targets.Add(target);
        _selectedTarget = Targets.FirstOrDefault();
        Document.TextChanged += (_, _) => IsDirty = Document.Text != _loadedText;
    }

    private static IEnumerable<ConfigTarget> BuildTargets(IRimeDistribution? distribution)
    {
        yield return new ConfigTarget("default.yaml", "default.yaml（引擎全局）");
        if (distribution?.UiConfigFile is { } uiFile)
            yield return new ConfigTarget(uiFile, $"{uiFile}（前端外观）");
    }

    public ObservableCollection<ConfigTarget> Targets { get; } = [];

    /// <summary>编辑器文档；载入替换整篇文本，保存直接取当前文本。</summary>
    public TextDocument Document { get; } = new();

    [ObservableProperty]
    private ConfigTarget? _selectedTarget;

    [ObservableProperty]
    private string _sourceFeedback = "";

    [ObservableProperty]
    private bool _isDirty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = "";

    public bool HasError => ErrorMessage.Length > 0;

    public bool IsAvailable => _distribution is not null;

    partial void OnSelectedTargetChanged(ConfigTarget? value) => LoadCommand.Execute(null);

    [RelayCommand]
    private void Load()
    {
        if (_store is null || SelectedTarget is null) return;

        ErrorMessage = "";
        try
        {
            var text = _store.ReadCustomText(SelectedTarget.BaseFile);
            Document.Text = text;
            _loadedText = text;
            IsDirty = false;
            SourceFeedback = _store.CustomFileExists(SelectedTarget.BaseFile)
                ? $"{Path.GetFileName(_store.CustomPath(SelectedTarget.BaseFile))} · 保存前自动留 .bak 备份"
                : "补丁文件尚不存在，保存时将创建";
        }
        catch (Exception ex)
        {
            ErrorMessage = $"读取配置失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task SaveAndDeployAsync()
    {
        if (_distribution is null || _store is null || SelectedTarget is null || IsBusy) return;

        IsBusy = true;
        try
        {
            ErrorMessage = "";
            // Write only when edited — a clean click still redeploys (the
            // button is always clickable, mirroring the settings page).
            var saved = IsDirty;
            var fileName = Path.GetFileName(_store.CustomPath(SelectedTarget.BaseFile));
            if (saved)
            {
                _store.WriteCustomText(SelectedTarget.BaseFile, Document.Text);
                _loadedText = Document.Text;
                IsDirty = false;
                SourceFeedback = $"已保存 {fileName}（{DateTime.Now:HH:mm:ss}）";
            }

            if (!_distribution.CanDeploy)
            {
                AppNotifications.Notify("设置已保存",
                    saved
                        ? $"已写入 {fileName}。{InstallHints.DeployerMissingHint(_distribution)}"
                        : $"本次没有修改需要写入。{InstallHints.DeployerMissingHint(_distribution)}",
                    NotificationType.Warning);
                return;
            }

            try
            {
                await _distribution.DeployAsync();
                AppNotifications.Toast(saved ? "已保存，重新部署完成" : "重新部署完成",
                    NotificationType.Success);
            }
            catch (Exception ex)
            {
                // The patch is already on disk — report the deploy problem
                // on its own instead of failing the whole save.
                AppNotifications.Notify("已保存，但重新部署失败",
                    $"{fileName} 已写入。{ex.Message} 稍后可重试。", NotificationType.Warning);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
            // The error banner sits below the editor; the save button is in
            // the top toolbar, so also surface the failure as a toast.
            AppNotifications.Toast($"保存失败：{ex.Message}", NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
