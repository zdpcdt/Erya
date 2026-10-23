using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Configuration;
using Erya.Core.Distributions;
using Erya.Core.Settings;
using Erya.Localization;
using Erya.Services;

namespace Erya.ViewModels.Settings;

public sealed partial class SettingsSectionViewModel : ObservableObject
{
    public SettingsSectionViewModel(SettingsSection section, Func<SettingsField, IReadOnlyList<string>>? dynamicChoices)
    {
        Title = section.Title;
        Description = section.Description;
        Group = section.Group;
        foreach (var field in section.Fields) Fields.Add(CreateField(field, dynamicChoices));
    }

    public string Title { get; }

    public string? Description { get; }

    /// <summary>页面归属（skin/layout/apps）；null = 无分组。</summary>
    public string? Group { get; }

    public ObservableCollection<SettingsFieldViewModel> Fields { get; } = [];

    private static SettingsFieldViewModel CreateField(SettingsField field, Func<SettingsField, IReadOnlyList<string>>? dynamicChoices)
    {
        switch (field.Type)
        {
            case "toggle":
                return new ToggleFieldViewModel(field);
            case "stepper" or "slider":
                return new NumberFieldViewModel(field);
            case "text":
                return new TextFieldViewModel(field);
            case "hotkey":
                return new HotkeyFieldViewModel(field);
            case "font":
                return new FontFieldViewModel(field);
            case "enum" or "segmented":
                var choices = field.Choices
                    ?? (field.ChoicesRef is { } refPath && dynamicChoices is not null ? dynamicChoices(field) : []);
                return new ChoiceFieldViewModel(field, choices, allowUnset: true);
            case "hotkeyList" or "hotkeyPairList":
                return new ListFieldViewModel(field);
            case "keyMapping":
                return new KeyMappingFieldViewModel(field);
            case "checkList":
                return new CheckListFieldViewModel(field);
            case "appOptions":
                return new AppOptionsFieldViewModel(field);
            default:
                throw new InvalidOperationException(
                    Loc.Format(LanguageManager.Instance.settings_unknownFieldType, field.Type));
        }
    }
}

/// <summary>
/// Schema-driven settings editor over one base config file. One instance
/// targets "default" (engine settings every frontend reads), another "ui"
/// (the frontend's own YAML — squirrel/weasel; Linux frontends have none and
/// show a hint instead). Values shown are base+patch merged; saves write only
/// user-dirty keys into the .custom.yaml patch, then redeploy.
/// </summary>
public sealed partial class SettingsEditorViewModel : ObservableObject
{
    private readonly IRimeDistribution? _distribution;
    private readonly string _target; // "default" | "ui"

    private SettingsSchema? _schema;
    private RimeConfigStore? _store;

    public SettingsEditorViewModel(IRimeDistribution? distribution, string target)
    {
        _distribution = distribution;
        _target = target;
    }

    public ObservableCollection<SettingsSectionViewModel> Sections { get; } = [];

    /// <summary>某页面组（skin/layout/apps；null=无分组）的分节视图，供拆分后的外观子页绑定。</summary>
    public IReadOnlyList<SettingsSectionViewModel> SectionsFor(string? group)
        => Sections.Where(s => s.Group == group).ToList();

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _unsupportedReason = "";

    [ObservableProperty]
    private string _errorMessage = "";

    [ObservableProperty]
    private string _subtitle = "";

    public bool HasError => ErrorMessage.Length > 0;

    public bool IsSupported => UnsupportedReason.Length == 0;

    /// <summary>前端自己的 UI 配置文件（weasel.yaml / squirrel.yaml）；Linux 前端为 null。</summary>
    public string? UiConfigFile => _distribution?.UiConfigFile;

    public bool IsDirty => Sections.SelectMany(s => s.Fields).Any(f => f.HasPendingEdits)
                           || (SchemeLibrary?.HasPendingEdits ?? false);

    /// <summary>皮肤库（仅外观目标且前端带 preset_color_schemes 时存在）。</summary>
    public ColorSchemeLibraryViewModel? SchemeLibrary { get; private set; }

    public bool HasSchemeLibrary => SchemeLibrary is not null;

    /// <summary>候选窗实时预览（仅外观目标）：亮/暗两个实例并排，固定在设置区上方。</summary>
    public CandidatePreviewViewModel? CandidatePreview { get; private set; }

    public CandidatePreviewViewModel? CandidatePreviewDark { get; private set; }

    public bool HasCandidatePreview => CandidatePreview is not null;

    [RelayCommand]
    private async Task LoadAsync()
    {
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            Sections.Clear();
            UnsupportedReason = ReasonUnavailable();
            if (_distribution is null || !IsSupported) return;

            _store ??= _distribution.GetConfigStore();
            _schema ??= _target == "ui"
                ? SettingsSchemas.LoadForUiConfig(_distribution.UiConfigFile!)
                : SettingsSchemas.LoadDefault();
            var targetFile = _schema.TargetFile;

            IReadOnlyList<string> ResolveChoices(SettingsField field)
            {
                var keys = field.ChoicesRef is { } refPath ? _store.ListMappingKeys(targetFile, refPath) : [];
                // Keep the current value selectable even when the base file
                // carrying the scheme definitions is missing on this machine.
                var current = _store.ReadMergedValue(targetFile, field.KeyPath)?.ToString();
                return current is { Length: > 0 } && !keys.Contains(current) ? [.. keys, current] : keys;
            }

            foreach (var section in _schema.Sections)
            {
                var sectionVm = new SettingsSectionViewModel(section, ResolveChoices);
                foreach (var fieldVm in sectionVm.Fields)
                {
                    fieldVm.PropertyChanged += OnFieldChanged;
                    fieldVm.Load(_store.ReadMergedValue(targetFile, fieldVm.KeyPath));
                }
                Sections.Add(sectionVm);
            }
            OnPropertyChanged(nameof(Sections)); // 子页经 SectionsFor 重新取列表

            Subtitle = Loc.Format(LanguageManager.Instance.settings_subtitleFormat,
                Path.GetFileName(_store.CustomPath(targetFile)));

            if (_target == "ui")
            {
                SchemeLibrary = new ColorSchemeLibraryViewModel(_store, targetFile);
                SchemeLibrary.EditsPending += (_, _) => OnPropertyChanged(nameof(IsDirty));
                SchemeLibrary.Load();
                OnPropertyChanged(nameof(HasSchemeLibrary));

                var fields = Sections.SelectMany(s => s.Fields).ToList();
                // 展开区的皮肤预览：排版参数取外观字段当前值（含未保存编辑）。
                SchemeLibrary.PreviewFactory = scheme =>
                {
                    var preview = new CandidatePreviewViewModel();
                    preview.AttachToScheme(fields, scheme);
                    return preview;
                };
                CandidatePreview ??= new CandidatePreviewViewModel();
                CandidatePreviewDark ??= new CandidatePreviewViewModel(preferDark: true);
                CandidatePreview.Attach(fields, SchemeLibrary);
                CandidatePreviewDark.Attach(fields, SchemeLibrary);
                OnPropertyChanged(nameof(HasCandidatePreview));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.Format(LanguageManager.Instance.settings_loadFailed, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string ReasonUnavailable()
    {
        if (_distribution is null)
            return Loc.Format(LanguageManager.Instance.settings_reasonNoDistribution, InstallHints.ForCurrentOs);
        if (_target == "ui" && _distribution.UiConfigFile is null)
            return _distribution.Id is "fcitx5-rime" or "ibus-rime"
                ? Loc.Format(LanguageManager.Instance.settings_reasonFrameworkPanel, _distribution.DisplayName)
                : Loc.Format(LanguageManager.Instance.settings_reasonNoUiConfig, _distribution.DisplayName);
        return "";
    }

    [RelayCommand]
    private async Task SaveAndDeployAsync()
    {
        if (_distribution is null || _store is null || _schema is null || IsBusy) return;

        var writes = new Dictionary<string, object?>();
        var removals = new List<string>();
        foreach (var field in Sections.SelectMany(s => s.Fields))
        {
            if (!field.HasPendingEdits) continue;
            var value = field.PendingValue();
            if (value is not null) writes[field.KeyPath] = value;
            else if (field.PendingRemoval()) removals.Add(field.KeyPath);
        }
        if (SchemeLibrary is { } library)
        {
            // Per-scheme flat keys (preset_color_schemes/<id>), never the
            // whole collection — a collection patch key REPLACES the shipped
            // schemes instead of merging into them.
            foreach (var (key, value) in library.PendingWrites())
                writes[key] = value;
            removals.AddRange(library.PendingRemovals());
        }
        // Nothing pending is not a no-op: the button stays clickable so the
        // user can always force a redeploy from here.
        IsBusy = true;
        try
        {
            ErrorMessage = "";
            var saved = writes.Count > 0 || removals.Count > 0;
            if (writes.Count > 0) _store.WritePatch(_schema.TargetFile, writes);
            if (removals.Count > 0) _store.RemovePatchKeys(_schema.TargetFile, removals);

            var customName = Path.GetFileName(_store.CustomPath(_schema.TargetFile));
            var m = LanguageManager.Instance;
            if (!_distribution.CanDeploy)
            {
                AppNotifications.Notify(Loc.Get(m.settings_savedTitle),
                    saved
                        ? Loc.Format(m.settings_savedNoDeployerWritten, customName,
                            InstallHints.DeployerMissingHint(_distribution))
                        : Loc.Format(m.settings_savedNoDeployerNothing, InstallHints.DeployerMissingHint(_distribution)),
                    NotificationType.Warning);
            }
            else
            {
                try
                {
                    await _distribution.DeployAsync();
                    AppNotifications.Toast(
                        saved ? Loc.Get(m.settings_savedDeployDone) : Loc.Get(m.settings_redeployDone),
                        NotificationType.Success);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    // The patch is already on disk — report the deploy problem
                    // on its own instead of failing the whole save.
                    AppNotifications.Notify(Loc.Get(m.settings_savedDeployFailedTitle),
                        Loc.Format(m.settings_savedDeployFailedDetail, customName, ex.Message),
                        NotificationType.Warning);
                }
            }
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.Format(LanguageManager.Instance.settings_saveFailed, ex.Message);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnFieldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsFieldViewModel.HasPendingEdits))
            OnPropertyChanged(nameof(IsDirty));
    }
}
