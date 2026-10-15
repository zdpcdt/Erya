using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Settings;

namespace Erya.ViewModels.Settings;

/// <summary>
/// One schema field bound to the UI. Subclasses own the control value; this
/// base owns the patch semantics: <see cref="IsModified"/> means the key
/// exists in the .custom.yaml patch (a badge), <see cref="HasPendingEdits"/>
/// means the user changed the control and a save would write (or remove) it.
/// </summary>
public abstract partial class SettingsFieldViewModel : ObservableObject
{
    protected SettingsFieldViewModel(SettingsField field) => Field = field;

    public SettingsField Field { get; }

    public string Label => Field.Label;

    public string? Description => Field.Description;

    public string KeyPath => Field.KeyPath;

    /// <summary>该键已写入 .custom.yaml 补丁（非发行版默认值）。</summary>
    [ObservableProperty]
    private bool _isModified;

    /// <summary>用户改动尚未保存。</summary>
    [ObservableProperty]
    private bool _hasPendingEdits;

    /// <summary>Assigned by the editor page: removes this field's patch entry.</summary>
    public System.Windows.Input.ICommand? UndoPatchCommand { get; set; }

    /// <summary>Fill the control from the merged value and patch presence.</summary>
    public abstract void Load(object? mergedValue, bool inPatch);

    /// <summary>Drop user edits and return to the loaded state.</summary>
    public abstract void Revert();

    /// <summary>The value a save should write; null = nothing pending.</summary>
    public abstract object? PendingValue();

    /// <summary>True when a save should REMOVE this key from the patch (back to base default).</summary>
    public virtual bool PendingRemoval() => PendingValue() is null && HasPendingEdits;

    protected static string? AsString(object? value) => value?.ToString();

    protected static double? AsNumber(object? value) => value switch
    {
        long l => l,
        int i => i,
        double d => d,
        _ => null,
    };

    protected static bool AsBool(object? value) => value is true;

    protected static IReadOnlyList<string> ToStringList(object? value)
        => value as IReadOnlyList<object?> is { } list
            ? list.Select(v => v?.ToString() ?? "").Where(s => s.Length > 0).ToList()
            : [];
}

public sealed partial class ToggleFieldViewModel : SettingsFieldViewModel
{
    private bool _loaded;

    public ToggleFieldViewModel(SettingsField field) : base(field) { }

    [ObservableProperty]
    private bool _isChecked;

    public override void Load(object? mergedValue, bool inPatch)
    {
        _loaded = AsBool(mergedValue) || (mergedValue is null && Field.DefaultValue() is true);
        IsChecked = _loaded;
        IsModified = inPatch;
        HasPendingEdits = false;
    }

    public override void Revert()
    {
        IsChecked = _loaded;
        HasPendingEdits = false;
    }

    public override object? PendingValue() => HasPendingEdits ? IsChecked : null;

    partial void OnIsCheckedChanged(bool value) => HasPendingEdits = value != _loaded;
}

public sealed partial class NumberFieldViewModel : SettingsFieldViewModel
{
    private double _loaded;

    public NumberFieldViewModel(SettingsField field) : base(field) { }

    [ObservableProperty]
    private double _value;

    public double Min => Field.Min ?? 0;
    public double Max => Field.Max ?? 100;
    public double Step => Field.Step ?? 1;
    public bool IsInteger => Step % 1 == 0;
    public string Format => IsInteger ? "0" : "0.##";

    public override void Load(object? mergedValue, bool inPatch)
    {
        _loaded = AsNumber(mergedValue) ?? AsNumber(Field.DefaultValue()) ?? Min;
        Value = _loaded;
        IsModified = inPatch;
        HasPendingEdits = false;
    }

    public override void Revert()
    {
        Value = _loaded;
        HasPendingEdits = false;
    }

    public override object? PendingValue() => HasPendingEdits
        ? IsInteger ? (long)Math.Round(Value) : Value
        : null;

    partial void OnValueChanged(double value) => HasPendingEdits = Math.Abs(value - _loaded) > 1e-9;
}

public sealed partial class TextFieldViewModel : SettingsFieldViewModel
{
    private string? _loaded;

    public TextFieldViewModel(SettingsField field) : base(field) { }

    [ObservableProperty]
    private string _text = "";

    public string? Placeholder => Field.DefaultValue()?.ToString();

    public override void Load(object? mergedValue, bool inPatch)
    {
        _loaded = AsString(mergedValue);
        Text = _loaded ?? "";
        IsModified = inPatch;
        HasPendingEdits = false;
    }

    public override void Revert()
    {
        Text = _loaded ?? "";
        HasPendingEdits = false;
    }

    public override object? PendingValue() => !HasPendingEdits ? null
        : Text.Length == 0 ? null        // cleared text = back to default → remove
        : Text;

    partial void OnTextChanged(string value) => HasPendingEdits = value != (_loaded ?? "");
}

/// <summary>enum / segmented: a choice list where null means “跟随默认（未设置）”。</summary>
public sealed partial class ChoiceFieldViewModel : SettingsFieldViewModel
{
    private string? _loaded;

    public ChoiceFieldViewModel(SettingsField field, IReadOnlyList<string> choices, bool allowUnset) : base(field)
    {
        Choices = choices;
        AllowUnset = allowUnset;
        Options = allowUnset ? [UnsetLabel, .. choices] : choices;
    }

    public IReadOnlyList<string> Choices { get; }

    public bool AllowUnset { get; }

    public string UnsetLabel => "未设置";

    /// <summary>Combo list including the explicit unset entry.</summary>
    public IReadOnlyList<string> Options { get; }

    [ObservableProperty]
    private string? _selected;

    /// <summary>Bind target for ComboBox — maps the unset entry to/from null.</summary>
    public string? SelectedOption
    {
        get => Selected is null ? UnsetLabel : Selected;
        set => Selected = value == UnsetLabel ? null : value;
    }

    public override void Load(object? mergedValue, bool inPatch)
    {
        _loaded = AsString(mergedValue);
        Selected = _loaded;
        IsModified = inPatch;
        HasPendingEdits = false;
    }

    public override void Revert()
    {
        Selected = _loaded;
        HasPendingEdits = false;
    }

    public override object? PendingValue() => HasPendingEdits ? Selected : null;

    partial void OnSelectedChanged(string? value)
    {
        HasPendingEdits = value != _loaded;
        OnPropertyChanged(nameof(SelectedOption));
    }
}

/// <summary>hotkeyList / hotkeyPairList: an editable list of key-combo strings.</summary>
public sealed partial class ListFieldViewModel : SettingsFieldViewModel
{
    private IReadOnlyList<string> _loaded = [];

    public ListFieldViewModel(SettingsField field) : base(field)
    {
        Items.CollectionChanged += (_, _) => RecomputeDirty();
    }

    public ObservableCollection<string> Items { get; } = [];

    public IReadOnlyList<string>? ItemLabels => Field.ItemLabels;

    [ObservableProperty]
    private string _newItem = "";

    public override void Load(object? mergedValue, bool inPatch)
    {
        _loaded = ToStringList(mergedValue);
        Items.Clear();
        // Fixed pairs always keep their slots so the file keeps the pair shape.
        if (ItemLabels is { } labels)
        {
            foreach (var item in _loaded) Items.Add(item);
            for (int i = _loaded.Count; i < labels.Count; i++) Items.Add("");
        }
        else
        {
            foreach (var item in _loaded) Items.Add(item);
        }
        IsModified = inPatch;
        HasPendingEdits = false;
    }

    public override void Revert()
    {
        Items.Clear();
        foreach (var item in _loaded) Items.Add(item);
        HasPendingEdits = false;
    }

    public override object? PendingValue() => !HasPendingEdits || Items.Count == 0
        ? null
        : Items.Select(i => (object?)i).ToList();

    [RelayCommand]
    private void AddItem()
    {
        var item = NewItem.Trim();
        if (item.Length == 0 || Items.Contains(item)) return;
        NewItem = "";
        Items.Add(item);
    }

    [RelayCommand]
    private void RemoveItem(string item) => Items.Remove(item);

    private void RecomputeDirty() => HasPendingEdits = !_loaded.SequenceEqual(Items);
}

public sealed partial class KeyMappingFieldViewModel : SettingsFieldViewModel
{
    public KeyMappingFieldViewModel(SettingsField field) : base(field)
    {
        foreach (var key in field.Keys ?? [])
        {
            var row = new KeyMappingRowViewModel(key);
            row.PropertyChanged += OnRowChanged;
            Rows.Add(row);
        }
    }

    public ObservableCollection<KeyMappingRowViewModel> Rows { get; } = [];

    public IReadOnlyList<string> Choices => Field.Choices ?? [];

    public string UnsetLabel => "未设置";

    public IReadOnlyList<string> Options => [UnsetLabel, .. Choices];

    public override void Load(object? mergedValue, bool inPatch)
    {
        var map = mergedValue as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();
        foreach (var row in Rows)
            row.LoadValue(map.TryGetValue(row.Key, out var value) ? value?.ToString() : null);
        IsModified = inPatch;
        HasPendingEdits = false;
    }

    public override void Revert()
    {
        foreach (var row in Rows) row.LoadValue(row.Selection);
        HasPendingEdits = false;
    }

    public override object? PendingValue()
    {
        if (!HasPendingEdits) return null;
        var selected = Rows.Where(r => r.Selection is { } choice)
            .ToDictionary(r => r.Key, r => (object?)r.Selection);
        return selected.Count == 0 ? null : selected;
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(KeyMappingRowViewModel.Selection)) return;
        HasPendingEdits = Rows.Any(r => r.IsDirty);
    }
}

public sealed partial class KeyMappingRowViewModel(string key) : ObservableObject
{
    public string Key { get; } = key;

    [ObservableProperty]
    private string? _selection;

    [ObservableProperty]
    private bool _isDirty;

    /// <summary>Sets the value without tripping the user-edit dirty flag (used by Load).</summary>
    public void LoadValue(string? value)
    {
        Selection = value;   // trips IsDirty via the changed hook…
        IsDirty = false;     // …which is exactly what loading must reset
    }

    /// <summary>Bind target for ComboBox — maps the unset entry to/from null.</summary>
    public string? SelectedOption
    {
        get => Selection is null ? UnsetLabel : Selection;
        set => Selection = value == UnsetLabel ? null : value;
    }

    public string UnsetLabel => "未设置";

    partial void OnSelectionChanged(string? value)
    {
        IsDirty = true;
        OnPropertyChanged(nameof(SelectedOption));
    }
}

public sealed partial class CheckListFieldViewModel : SettingsFieldViewModel
{
    private IReadOnlyList<string> _loaded = [];

    public CheckListFieldViewModel(SettingsField field) : base(field)
    {
        foreach (var choice in field.Choices ?? [])
        {
            var option = new CheckOptionViewModel(choice);
            option.PropertyChanged += OnOptionChanged;
            Options.Add(option);
        }
    }

    public ObservableCollection<CheckOptionViewModel> Options { get; } = [];

    public override void Load(object? mergedValue, bool inPatch)
    {
        _loaded = ToStringList(mergedValue);
        foreach (var option in Options) option.IsChecked = _loaded.Contains(option.Value);
        IsModified = inPatch;
        HasPendingEdits = false;
    }

    public override void Revert()
    {
        foreach (var option in Options) option.IsChecked = _loaded.Contains(option.Value);
        HasPendingEdits = false;
    }

    public override object? PendingValue()
        => !HasPendingEdits || Options.All(o => !o.IsChecked)
            ? null
            : Options.Where(o => o.IsChecked).Select(o => (object?)o.Value).ToList();

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(CheckOptionViewModel.IsChecked)) return;
        HasPendingEdits = !_loaded.SequenceEqual(Options.Where(o => o.IsChecked).Select(o => o.Value));
    }
}

public sealed partial class CheckOptionViewModel(string value) : ObservableObject
{
    public string Value { get; } = value;

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>app_options: per-app input behavior rows (bundle ID / exe name).</summary>
public sealed partial class AppOptionsFieldViewModel : SettingsFieldViewModel
{
    public AppOptionsFieldViewModel(SettingsField field) : base(field) { }

    public ObservableCollection<AppOptionsRowViewModel> Rows { get; } = [];

    private IReadOnlyDictionary<string, object?> _loadedMap = new Dictionary<string, object?>();

    public override void Load(object? mergedValue, bool inPatch)
    {
        _loadedMap = mergedValue as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();
        BuildRows(_loadedMap);
        IsModified = inPatch;
        HasPendingEdits = false;
    }

    public override void Revert()
    {
        BuildRows(_loadedMap);
        HasPendingEdits = false;
    }

    private void BuildRows(IReadOnlyDictionary<string, object?> map)
    {
        foreach (var row in Rows) row.Changed -= MarkDirty;
        Rows.Clear();
        foreach (var (app, options) in map)
        {
            var dict = options as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();
            var row = new AppOptionsRowViewModel(app)
            {
                AsciiMode = dict.TryGetValue("ascii_mode", out var ascii) && ascii is true,
                VimMode = dict.TryGetValue("vim_mode", out var vim) && vim is true,
            };
            row.Changed += MarkDirty;
            Rows.Add(row);
        }
    }

    public override object? PendingValue()
    {
        if (!HasPendingEdits) return null;
        var apps = Rows
            .Select(r => r.AppId.Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Select(id => new { Id = id, Row = Rows.First(r => r.AppId.Trim() == id) })
            .ToDictionary(
                app => app.Id,
                app => (object?)new Dictionary<string, object?>
                {
                    ["ascii_mode"] = app.Row.AsciiMode,
                    ["vim_mode"] = app.Row.VimMode,
                });
        return apps.Count == 0 ? null : apps;
    }

    [RelayCommand]
    private void AddRow()
    {
        var row = new AppOptionsRowViewModel("");
        row.Changed += MarkDirty;
        Rows.Add(row);
        MarkDirty();
    }

    [RelayCommand]
    private void RemoveRow(AppOptionsRowViewModel row)
    {
        row.Changed -= MarkDirty;
        Rows.Remove(row);
        MarkDirty();
    }

    private void MarkDirty() => HasPendingEdits = true;
}

public sealed partial class AppOptionsRowViewModel : ObservableObject
{
    public AppOptionsRowViewModel(string appId) => _appId = appId;

    /// <summary>Fires on any edit (including the id text) — saving collapses rows anyway.</summary>
    public event Action? Changed;

    [ObservableProperty]
    private string _appId;

    [ObservableProperty]
    private bool _asciiMode;

    [ObservableProperty]
    private bool _vimMode;

    partial void OnAppIdChanged(string value) => Changed?.Invoke();
    partial void OnAsciiModeChanged(bool value) => Changed?.Invoke();
    partial void OnVimModeChanged(bool value) => Changed?.Invoke();
}
