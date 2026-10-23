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
using Erya.Localization;
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
        var m = LanguageManager.Instance;
        yield return new ConfigTarget("default.yaml", Loc.Get(m.advanced_targetEngine));
        if (distribution?.UiConfigFile is { } uiFile)
            yield return new ConfigTarget(uiFile, Loc.Format(m.advanced_targetFrontend, uiFile));
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
                ? Loc.Format(LanguageManager.Instance.advanced_patchExistsFeedback,
                    Path.GetFileName(_store.CustomPath(SelectedTarget.BaseFile)))
                : Loc.Get(LanguageManager.Instance.advanced_patchMissingFeedback);
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.Format(LanguageManager.Instance.advanced_loadFailed, ex.Message);
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
            var m = LanguageManager.Instance;
            // Write only when edited — a clean click still redeploys (the
            // button is always clickable, mirroring the settings page).
            var saved = IsDirty;
            var fileName = Path.GetFileName(_store.CustomPath(SelectedTarget.BaseFile));
            if (saved)
            {
                _store.WriteCustomText(SelectedTarget.BaseFile, Document.Text);
                _loadedText = Document.Text;
                IsDirty = false;
                SourceFeedback = Loc.Format(m.advanced_savedFeedback, fileName, DateTime.Now.ToString("HH:mm:ss"));
            }

            if (!_distribution.CanDeploy)
            {
                AppNotifications.Notify(Loc.Get(m.advanced_savedTitle),
                    saved
                        ? Loc.Format(m.advanced_savedWrittenDetail, fileName,
                            InstallHints.DeployerMissingHint(_distribution))
                        : Loc.Format(m.advanced_savedNoChangeDetail,
                            InstallHints.DeployerMissingHint(_distribution)),
                    NotificationType.Warning);
                return;
            }

            try
            {
                await _distribution.DeployAsync();
                AppNotifications.Toast(
                    saved ? Loc.Get(m.advanced_savedAndDeployedToast) : Loc.Get(m.advanced_deployedToast),
                    NotificationType.Success);
            }
            catch (Exception ex)
            {
                // The patch is already on disk — report the deploy problem
                // on its own instead of failing the whole save.
                AppNotifications.Notify(Loc.Get(m.advanced_deployFailedTitle),
                    Loc.Format(m.advanced_deployFailedDetail, fileName, ex.Message), NotificationType.Warning);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.Format(LanguageManager.Instance.advanced_saveFailed, ex.Message);
            // The error banner sits below the editor; the save button is in
            // the top toolbar, so also surface the failure as a toast.
            AppNotifications.Toast(Loc.Format(LanguageManager.Instance.advanced_saveFailed, ex.Message),
                NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }
}
