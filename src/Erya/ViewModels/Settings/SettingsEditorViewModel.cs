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
using Erya.Services;

namespace Erya.ViewModels.Settings;

public sealed partial class SettingsSectionViewModel : ObservableObject
{
    public SettingsSectionViewModel(SettingsSection section, Func<SettingsField, IReadOnlyList<string>>? dynamicChoices)
    {
        Title = section.Title;
        Description = section.Description;
        foreach (var field in section.Fields) Fields.Add(CreateField(field, dynamicChoices));
    }

    public string Title { get; }

    public string? Description { get; }

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
                throw new InvalidOperationException($"未知字段类型：{field.Type}");
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

    public bool IsDirty => Sections.SelectMany(s => s.Fields).Any(f => f.HasPendingEdits)
                           || (SchemeLibrary?.HasPendingEdits ?? false);

    /// <summary>皮肤库（仅外观目标且前端带 preset_color_schemes 时存在）。</summary>
    public ColorSchemeLibraryViewModel? SchemeLibrary { get; private set; }

    public bool HasSchemeLibrary => SchemeLibrary is not null;

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
            var patchKeys = _store.ReadPatchValues(targetFile);

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
                    fieldVm.UndoPatchCommand = UndoPatchKeyCommand;
                    fieldVm.Load(_store.ReadMergedValue(targetFile, fieldVm.KeyPath), patchKeys.ContainsKey(fieldVm.KeyPath));
                }
                Sections.Add(sectionVm);
            }

            Subtitle = $"显示合并后的生效值；修改会保存到 {Path.GetFileName(_store.CustomPath(targetFile))} 补丁，重新部署后生效。";

            if (_target == "ui")
            {
                SchemeLibrary = new ColorSchemeLibraryViewModel(_store, targetFile);
                SchemeLibrary.EditsPending += (_, _) => OnPropertyChanged(nameof(IsDirty));
                SchemeLibrary.Load();
                OnPropertyChanged(nameof(HasSchemeLibrary));
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"读取设置失败：{ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private string ReasonUnavailable()
    {
        if (_distribution is null)
            return "未检测到 RIME 发行版：" + InstallHints.ForCurrentOs;
        if (_target == "ui" && _distribution.UiConfigFile is null)
            return _distribution.Id is "fcitx5-rime" or "ibus-rime"
                ? $"当前前端（{_distribution.DisplayName}）的候选面板外观由输入法框架自身设置（fcitx5 在「配置 → 附加组件 → 经典用户界面」中调整），RIME YAML 中没有对应配置。中英切换、标点等输入行为请在「输入设置」页调整。"
                : $"当前前端（{_distribution.DisplayName}）没有可配置的外观 YAML。";
        return "";
    }

    [RelayCommand]
    private void Revert()
    {
        // Capture before the field events re-raise IsDirty mid-revert.
        var hadPending = IsDirty;
        foreach (var field in Sections.SelectMany(s => s.Fields)) field.Revert();
        SchemeLibrary?.Revert();
        OnPropertyChanged(nameof(IsDirty));
        AppNotifications.Toast(hadPending ? "已还原未保存的修改。" : "当前没有未保存的修改。",
            hadPending ? NotificationType.Success : NotificationType.Information);
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
            if (!_distribution.CanDeploy)
            {
                AppNotifications.Notify("设置已保存",
                    saved
                        ? $"已写入 {customName}。{InstallHints.DeployerMissingHint(_distribution)}"
                        : $"本次没有修改需要写入。{InstallHints.DeployerMissingHint(_distribution)}",
                    NotificationType.Warning);
            }
            else
            {
                try
                {
                    await _distribution.DeployAsync();
                    AppNotifications.Toast(saved ? "设置已保存并触发重新部署。" : "已触发重新部署。",
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
                    AppNotifications.Notify("设置已保存，但重新部署失败",
                        $"已写入 {customName}。{ex.Message} 稍后可按 Ctrl+R 重试。",
                        NotificationType.Warning);
                }
            }
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>Remove one field's patch entry (back to the shipped default), then reload.</summary>
    [RelayCommand]
    private async Task UndoPatchKeyAsync(SettingsFieldViewModel? field)
    {
        if (field is null || _store is null || _schema is null || IsBusy) return;

        IsBusy = true;
        try
        {
            ErrorMessage = "";
            _store.RemovePatchKeys(_schema.TargetFile, [field.KeyPath]);
            AppNotifications.Toast($"已撤销 {field.KeyPath} 的补丁，重新部署后生效。", NotificationType.Success);
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"撤销失败：{ex.Message}";
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
