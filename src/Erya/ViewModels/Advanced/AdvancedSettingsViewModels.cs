using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;
using Erya.Services;
using YamlDotNet.RepresentationModel;

namespace Erya.ViewModels.Advanced;

/// <summary>One config file target in 高级设置 (default.yaml always; the frontend YAML when it has one).</summary>
public sealed record ConfigTarget(string BaseFile, string Title)
{
    public override string ToString() => Title;
}

/// <summary>
/// One node of the merged config tree. Scalars are editable in place and can
/// undo their patch entry; containers show their children. Visibility and
/// expansion are driven by the search / only-modified filters.
/// </summary>
public sealed partial class ConfigNodeViewModel : ObservableObject
{
    private readonly AdvancedSettingsViewModel _owner;

    public ConfigNodeViewModel(AdvancedSettingsViewModel owner, string key, string path, YamlNode node, bool patched)
    {
        _owner = owner;
        Key = key;
        Path = path;
        IsModified = patched;

        switch (node)
        {
            case YamlMappingNode map:
                Preview = $"{{…}} {map.Children.Count} 项";
                foreach (var (k, v) in map.Children)
                {
                    if (k is YamlScalarNode { Value: { } childKey })
                        Children.Add(new ConfigNodeViewModel(owner, childKey, $"{path}/{childKey}", v, owner.IsPatched($"{path}/{childKey}")));
                }
                break;
            case YamlSequenceNode seq:
                Preview = $"[…] {seq.Children.Count} 项";
                for (int i = 0; i < seq.Children.Count; i++)
                    Children.Add(new ConfigNodeViewModel(owner, $"[{i}]", $"{path}/{i}", seq[i], false));
                break;
            default:
                Preview = node is YamlScalarNode { Value: { } value } ? value : "null";
                IsEditable = true;
                _originalType = node is YamlScalarNode { Value: { } raw } ? ParseLike(raw) : null;
                break;
        }
    }

    public string Key { get; }

    /// <summary>Flatten path from the config root, e.g. "switcher/hotkeys".</summary>
    public string Path { get; }

    /// <summary>Short value preview for scalars, shape hint for containers.</summary>
    public string Preview { get; }

    /// <summary>This exact path is patched in the .custom.yaml file.</summary>
    public bool IsModified { get; }

    public bool IsEditable { get; }

    public bool HasChildren => Children.Count > 0;

    public ObservableCollection<ConfigNodeViewModel> Children { get; } = [];

    private readonly object? _originalType; // bool/long/double marker for typed scalar writes

    [ObservableProperty]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _isVisible = true;

    [ObservableProperty]
    private bool _isEditing;

    [ObservableProperty]
    private string _editText = "";

    public ICommand BeginEditCommand => _owner.BeginEditCommand;

    public ICommand ApplyEditCommand => _owner.ApplyEditCommand;

    public ICommand CancelEditCommand => _owner.CancelEditCommand;

    public ICommand UndoPatchCommand => _owner.UndoNodePatchCommand;

    /// <summary>Typed parse of the edited text: keeps booleans and numbers YAML-native.</summary>
    public object ParseEditedValue()
    {
        var text = EditText.Trim();
        if (_originalType is bool or long or double)
        {
            if (text is "true" or "false") return text == "true";
            if (_originalType is long && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l)) return l;
            if (_originalType is double && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var d)) return d;
            if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var li)) return li;
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var di)) return di;
        }
        return text;
    }

    /// <summary>Evaluates filter visibility; expands containers whose descendants match.</summary>
    public bool ApplyFilter(string? query, bool onlyModified)
    {
        bool selfMatches = (!onlyModified || IsModified)
            && (query is null || query.Length == 0
                || Path.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (Preview ?? "").Contains(query, StringComparison.OrdinalIgnoreCase));

        bool descendantMatches = false;
        foreach (var child in Children)
            if (child.ApplyFilter(query, onlyModified)) descendantMatches = true;

        IsVisible = selfMatches || descendantMatches;
        IsExpanded = IsVisible && descendantMatches && query is { Length: > 0 };
        return IsVisible;
    }

    private static object? ParseLike(string text) => text switch
    {
        "true" or "false" => text == "true",
        _ when long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out _) => 0L,
        _ when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out _) => 0d,
        _ => null,
    };
}

/// <summary>
/// 高级设置：browse the merged config tree (base + patch), edit any scalar
/// (writes land in the patch), undo patch entries per key, and switch to
/// source mode to edit the .custom.yaml file directly.
/// </summary>
public sealed partial class AdvancedSettingsViewModel : ObservableObject
{
    private readonly IRimeDistribution? _distribution;
    private readonly Core.Configuration.RimeConfigStore? _store;
    private readonly Dictionary<string, object?> _patchValues = new();

    public AdvancedSettingsViewModel(IRimeDistribution? distribution)
    {
        _distribution = distribution;
        _store = distribution?.GetConfigStore();
        foreach (var target in BuildTargets(distribution))
            Targets.Add(target);
        _selectedTarget = Targets.FirstOrDefault();
    }

    private static IEnumerable<ConfigTarget> BuildTargets(IRimeDistribution? distribution)
    {
        yield return new ConfigTarget("default.yaml", "default.yaml（引擎全局）");
        if (distribution?.UiConfigFile is { } uiFile)
            yield return new ConfigTarget(uiFile, $"{uiFile}（前端外观）");
    }

    public ObservableCollection<ConfigTarget> Targets { get; } = [];

    public ObservableCollection<ConfigNodeViewModel> Roots { get; } = [];

    [ObservableProperty]
    private ConfigTarget? _selectedTarget;

    [ObservableProperty]
    private string _searchText = "";

    [ObservableProperty]
    private bool _onlyModified;

    [ObservableProperty]
    private bool _isSourceMode;

    [ObservableProperty]
    private string _sourceText = "";

    [ObservableProperty]
    private string _sourceFeedback = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _errorMessage = "";

    public bool HasError => ErrorMessage.Length > 0;

    public bool HasTree => Roots.Count > 0;

    public bool IsAvailable => _distribution is not null;

    internal bool IsPatched(string path) => _patchValues.ContainsKey(path);

    [RelayCommand]
    private async Task LoadAsync()
    {
        if (_distribution is null || _store is null || SelectedTarget is null) return;

        ErrorMessage = "";
        try
        {
            _patchValues.Clear();
            foreach (var (key, value) in _store.ReadPatchValues(SelectedTarget.BaseFile))
                _patchValues[key] = value;

            Roots.Clear();
            if (_store.LoadMerged(SelectedTarget.BaseFile) is { } merged)
            {
                foreach (var (k, v) in merged.Children)
                {
                    if (k is YamlScalarNode { Value: { } key })
                        Roots.Add(new ConfigNodeViewModel(this, key, key, v, IsPatched(key)));
                }
            }
            ApplyFilter();
            OnPropertyChanged(nameof(HasTree));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"读取配置失败：{ex.Message}";
        }
        await Task.CompletedTask;
    }

    partial void OnSelectedTargetChanged(ConfigTarget? value) => LoadCommand.Execute(null);

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnOnlyModifiedChanged(bool value) => ApplyFilter();

    partial void OnIsSourceModeChanged(bool value)
    {
        if (value) LoadSource();
    }

    private void ApplyFilter()
    {
        var query = SearchText?.Trim();
        if (query?.Length == 0) query = null;
        foreach (var root in Roots) root.ApplyFilter(query, OnlyModified);
    }

    [RelayCommand]
    private void BeginEdit(ConfigNodeViewModel? node)
    {
        if (node is not { IsEditable: true }) return;
        foreach (var other in Roots.SelectMany(Walk)) other.IsEditing = false;
        node.EditText = node.Preview;
        node.IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit(ConfigNodeViewModel? node)
    {
        if (node is null) return;
        node.IsEditing = false;
    }

    [RelayCommand]
    private async Task ApplyEditAsync(ConfigNodeViewModel? node)
    {
        if (node is null || _store is null || SelectedTarget is null) return;

        IsBusy = true;
        try
        {
            ErrorMessage = "";
            _store.WritePatch(SelectedTarget.BaseFile, new Dictionary<string, object?> { [node.Path] = node.ParseEditedValue() });
            node.IsEditing = false;
            await LoadAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"写入失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UndoNodePatchAsync(ConfigNodeViewModel? node)
    {
        if (node is null || _store is null || SelectedTarget is null) return;

        IsBusy = true;
        try
        {
            ErrorMessage = "";
            _store.RemovePatchKeys(SelectedTarget.BaseFile, [node.Path]);
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

    // ---------- source mode ----------

    [RelayCommand]
    private void LoadSource()
    {
        if (_store is null || SelectedTarget is null) return;
        SourceText = _store.ReadCustomText(SelectedTarget.BaseFile);
        SourceFeedback = _store.CustomFileExists(SelectedTarget.BaseFile)
            ? $"正在编辑 {Path.GetFileName(_store.CustomPath(SelectedTarget.BaseFile))}；保存前会自动留 .bak 备份。"
            : "补丁文件尚不存在，保存时将创建。";
    }

    [RelayCommand]
    private async Task SaveSourceAsync()
    {
        if (_store is null || SelectedTarget is null || _distribution is null) return;

        IsBusy = true;
        try
        {
            ErrorMessage = "";
            _store.WriteCustomText(SelectedTarget.BaseFile, SourceText);
            SourceFeedback = $"已保存 {Path.GetFileName(_store.CustomPath(SelectedTarget.BaseFile))}。";
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

    [RelayCommand]
    private async Task DeployAsync()
    {
        if (_distribution is null) return;
        try
        {
            await _distribution.DeployAsync();
            AppNotifications.Toast("已触发重新部署。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            AppNotifications.Toast($"部署失败：{ex.Message}", NotificationType.Error);
        }
    }

    private static IEnumerable<ConfigNodeViewModel> Walk(ConfigNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Walk)) yield return child;
    }
}
