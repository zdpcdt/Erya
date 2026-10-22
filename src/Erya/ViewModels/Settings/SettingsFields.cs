using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Settings;
using Erya.Services;

namespace Erya.ViewModels.Settings;

/// <summary>
/// One schema field bound to the UI. Subclasses own the control value; this
/// base owns the patch semantics: <see cref="HasPendingEdits"/> means the
/// user changed the control and a save would write (or remove) it.
/// </summary>
public abstract partial class SettingsFieldViewModel : ObservableObject
{
    protected SettingsFieldViewModel(SettingsField field) => Field = field;

    public SettingsField Field { get; }

    public string Label => Field.Label;

    public string? Description => Field.Description;

    public string KeyPath => Field.KeyPath;

    /// <summary>行首图标声明（td:/ip:/semi: 前缀），经 FieldIconConverter 变成图标。</summary>
    public string? IconName => Field.Icon;

    /// <summary>用户改动尚未保存。</summary>
    [ObservableProperty]
    private bool _hasPendingEdits;

    /// <summary>Fill the control from the merged value and patch presence.</summary>
    public abstract void Load(object? mergedValue);

    /// <summary>The value a save should write; null = nothing pending.</summary>
    public abstract object? PendingValue();

    /// <summary>
    /// The control's value as it stands — pending or not; null = 未设置.
    /// What the候选窗预览 renders: it must reflect unsaved edits too.
    /// </summary>
    public virtual object? CurrentValue() => PendingValue();

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

    public override void Load(object? mergedValue)
    {
        _loaded = AsBool(mergedValue) || (mergedValue is null && Field.DefaultValue() is true);
        IsChecked = _loaded;
        HasPendingEdits = false;
    }

    public override object? PendingValue() => HasPendingEdits ? IsChecked : null;

    public override object? CurrentValue() => IsChecked;

    partial void OnIsCheckedChanged(bool value) => HasPendingEdits = value != _loaded;
}

public sealed partial class NumberFieldViewModel : SettingsFieldViewModel
{
    private double _loaded;
    private bool _loadedHadValue; // false = base/patch 里都没有，控件只是显示 Min

    public NumberFieldViewModel(SettingsField field) : base(field) { }

    [ObservableProperty]
    private double _value;

    public double Min => Field.Min ?? 0;
    public double Max => Field.Max ?? 100;
    public double Step => Field.Step ?? 1;
    public bool IsInteger => Step % 1 == 0;
    public string Format => IsInteger ? "0" : "0.##";
    public bool IsStepper => Field.Type == "stepper";
    public bool IsSlider => Field.Type == "slider";

    /// <summary>滑块行的右侧数值摘要（步进器自带数字框，不需要）。</summary>
    public string? Note => IsSlider ? Value.ToString("0.#") : null;

    public override void Load(object? mergedValue)
    {
        _loadedHadValue = AsNumber(mergedValue) is not null || Field.DefaultValue() is not null;
        _loaded = AsNumber(mergedValue) ?? AsNumber(Field.DefaultValue()) ?? Min;
        Value = _loaded;
        HasPendingEdits = false;
    }

    public override object? PendingValue() => HasPendingEdits
        ? IsInteger ? (long)Math.Round(Value) : Value
        : null;

    /// <summary>未设置时返回 null——候选窗预览需要区分「0」与「没配置」。</summary>
    public override object? CurrentValue()
        => HasPendingEdits || _loadedHadValue ? (IsInteger ? (long)Math.Round(Value) : Value) : null;

    partial void OnValueChanged(double value)
    {
        HasPendingEdits = Math.Abs(value - _loaded) > 1e-9;
        OnPropertyChanged(nameof(Note));
    }
}

public partial class TextFieldViewModel : SettingsFieldViewModel
{
    private string? _loaded;

    public TextFieldViewModel(SettingsField field) : base(field) { }

    [ObservableProperty]
    private string _text = "";

    public string? Placeholder => Field.DefaultValue()?.ToString();

    public override void Load(object? mergedValue)
    {
        _loaded = AsString(mergedValue);
        Text = _loaded ?? "";
        HasPendingEdits = false;
    }

    public override object? PendingValue() => !HasPendingEdits ? null
        : Text.Length == 0 ? null        // cleared text = back to default → remove
        : Text;

    partial void OnTextChanged(string value) => HasPendingEdits = value != (_loaded ?? "");

    public override object? CurrentValue() => Text.Length == 0 ? null : Text;
}

/// <summary>
/// font: same patch semantics as text, but the editor is a ComboBox fed by
/// this machine's installed font families plus an explicit 未设置 entry; the
/// loaded value stays selectable even when this host can't enumerate it (e.g.
/// weasel's comma fallback lists). Fixed width in the UI keeps a long merged
/// default from stretching the row.
/// </summary>
public sealed partial class FontFieldViewModel : TextFieldViewModel
{
    private IReadOnlyList<string>? _options;

    public FontFieldViewModel(SettingsField field) : base(field)
    {
        // Text is the store; the ComboBox binds the SelectedOption wrapper.
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(Text))
                OnPropertyChanged(nameof(SelectedOption));
        };
    }

    public string UnsetLabel => "未设置";

    /// <summary>
    /// Built once per Load (which runs before the field is bound) and kept
    /// stable afterwards: rebuilding on selection would hand the ComboBox a
    /// NEW ItemsSource instance, it clears its selection and writes null back
    /// through the SelectedItem binding — the「选中字体报空引用」regression.
    /// </summary>
    public IReadOnlyList<string> Options => _options ??= BuildOptions();

    /// <summary>Bind target for ComboBox — maps the unset entry to/from empty text.</summary>
    public string SelectedOption
    {
        get => Text.Length == 0 ? UnsetLabel : Text;
        // null-tolerant on purpose: a ComboBox whose items reset writes null.
        set => Text = string.IsNullOrEmpty(value) || value == UnsetLabel ? "" : value;
    }

    public override void Load(object? mergedValue)
    {
        base.Load(mergedValue);
        _options = BuildOptions();
        OnPropertyChanged(nameof(Options));
    }

    private IReadOnlyList<string> BuildOptions()
    {
        var fonts = SystemFontCatalog.Names;
        var loaded = Text.Trim();
        return loaded.Length > 0 && !fonts.Contains(loaded)
            ? [UnsetLabel, loaded, .. fonts]
            : [UnsetLabel, .. fonts];
    }
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

    public override void Load(object? mergedValue)
    {
        _loaded = AsString(mergedValue);
        Selected = _loaded;
        HasPendingEdits = false;
    }

    public override object? PendingValue() => HasPendingEdits ? Selected : null;

    public override object? CurrentValue() => Selected;

    partial void OnSelectedChanged(string? value)
    {
        HasPendingEdits = value != _loaded;
        OnPropertyChanged(nameof(SelectedOption));
    }
}

/// <summary>hotkeyList 中的一行：可观察，供 KeyGestureInput 双向绑定写回。</summary>
public sealed partial class HotkeyRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _value = "";
}

/// <summary>hotkeyList / hotkeyPairList: an editable list of key-combo rows.</summary>
public sealed partial class ListFieldViewModel : SettingsFieldViewModel
{
    private IReadOnlyList<string> _loaded = [];

    public ListFieldViewModel(SettingsField field) : base(field) { }

    public ObservableCollection<HotkeyRowViewModel> Items { get; } = [];

    public IReadOnlyList<string>? ItemLabels => Field.ItemLabels;

    [ObservableProperty]
    private string _newItem = "";

    public override void Load(object? mergedValue)
    {
        _loaded = ToStringList(mergedValue);
        Items.Clear();
        // Fixed pairs always keep their slots so the file keeps the pair shape.
        if (ItemLabels is { } labels)
        {
            foreach (var item in _loaded) AddRow(item);
            for (int i = _loaded.Count; i < labels.Count; i++) AddRow("");
        }
        else
        {
            foreach (var item in _loaded) AddRow(item);
        }
        HasPendingEdits = false;
    }

    public override object? PendingValue() => !HasPendingEdits || Items.Count == 0
        ? null
        : Items.Select(i => (object?)i.Value).ToList();

    [RelayCommand]
    private void AddItem()
    {
        var item = NewItem.Trim();
        if (item.Length == 0 || Items.Any(i => i.Value == item)) return;
        NewItem = "";
        AddRow(item);
        RecomputeDirty();
    }

    [RelayCommand]
    private void RemoveItem(HotkeyRowViewModel row)
    {
        row.PropertyChanged -= OnRowChanged;
        Items.Remove(row);
        RecomputeDirty();
    }

    private void AddRow(string value)
    {
        var row = new HotkeyRowViewModel { Value = value };
        row.PropertyChanged += OnRowChanged;
        Items.Add(row);
    }

    private void OnRowChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(HotkeyRowViewModel.Value))
            RecomputeDirty();
    }

    private void RecomputeDirty() => HasPendingEdits = !_loaded.SequenceEqual(Items.Select(i => i.Value));
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

    /// <summary>行右侧摘要：已自定义的键位数。</summary>
    public string Summary
    {
        get
        {
            var count = Rows.Count(r => r.Selection is not null);
            return count == 0 ? "未设置" : $"已设 {count} 项";
        }
    }

    public override void Load(object? mergedValue)
    {
        var map = mergedValue as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>();
        foreach (var row in Rows)
            row.LoadValue(map.TryGetValue(row.Key, out var value) ? value?.ToString() : null);
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
        OnPropertyChanged(nameof(Summary));
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

    /// <summary>行右侧摘要：已启用的选项数。</summary>
    public string EnabledSummary
    {
        get
        {
            var count = Options.Count(o => o.IsChecked);
            return count == 0 ? "未启用" : $"已启用 {count} 项";
        }
    }

    public override void Load(object? mergedValue)
    {
        _loaded = ToStringList(mergedValue);
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
        OnPropertyChanged(nameof(EnabledSummary));
    }
}

public sealed partial class CheckOptionViewModel(string value) : ObservableObject
{
    public string Value { get; } = value;

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>
/// <summary>
/// app_options：每应用一条输入规则（Windows 为 exe 名，macOS 为 Bundle ID）。
/// 可编辑选项集来自 schema 的 choices——两个前端读的键不同（鼠须管
/// inline/no_inline，小狼毫 inline_preedit）；复选框没覆盖的键保存时原样带回，
/// 「由开改关」写出显式 false 才能盖过基座默认值。没有任何键的应用不写入，
/// 删光所有应用则整个键从 patch 移除。应用行为页以专用卡片列表渲染本字段。
/// </summary>
public sealed partial class AppOptionsFieldViewModel : SettingsFieldViewModel
{
    private readonly IReadOnlyList<string> _editableKeys;

    public AppOptionsFieldViewModel(SettingsField field) : base(field)
    {
        _editableKeys = field.Choices is { Count: > 0 } choices ? choices : ["ascii_mode", "vim_mode"];
    }

    public ObservableCollection<AppOptionsRowViewModel> Rows { get; } = [];

    public override void Load(object? mergedValue)
    {
        BuildRows(mergedValue as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>());
        HasPendingEdits = false;
    }

    private void BuildRows(IReadOnlyDictionary<string, object?> map)
    {
        foreach (var row in Rows) row.Changed -= MarkDirty;
        Rows.Clear();
        foreach (var (app, options) in map)
            Attach(new AppOptionsRowViewModel(app, _editableKeys,
                options as IReadOnlyDictionary<string, object?> ?? new Dictionary<string, object?>()));
    }

    public override object? PendingValue()
    {
        if (!HasPendingEdits) return null;
        var apps = Rows
            .Select(r => r.AppId.Trim())
            .Where(id => id.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .Select(id => (Id: id, Value: Rows.First(r => r.AppId.Trim() == id).BuildValue()))
            .Where(app => app.Value.Count > 0)
            .ToDictionary(app => app.Id, app => (object?)app.Value);
        // 全部删光（或都无键）→ null：基类 PendingRemoval 会把整段键从 patch 移除。
        return apps.Count == 0 ? null : apps;
    }

    private void Attach(AppOptionsRowViewModel row)
    {
        row.Changed += MarkDirty;
        Rows.Add(row);
    }

    /// <summary>添加应用（空/重复标识返回错误文案，null = 已添加）；对话框确定后调用。</summary>
    public string? AddApp(string appId, bool asciiMode)
    {
        var id = appId.Trim();
        if (id.Length == 0) return "请填写应用标识。";
        if (Rows.Any(r => string.Equals(r.AppId.Trim(), id, StringComparison.OrdinalIgnoreCase)))
            return $"已有 {id} 的规则。";
        var row = new AppOptionsRowViewModel(id, _editableKeys, new Dictionary<string, object?>());
        if (asciiMode)
            row.Options.FirstOrDefault(o => o.Key == "ascii_mode")?.IsChecked = true;
        Attach(row);
        MarkDirty();
        return null;
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

/// <summary>
/// 一条应用规则；ID 不再提供行内编辑（改需求罕见，删了重加）。摘要文案随
/// 选项勾选联动，卡片说明行直接绑定。
/// </summary>
public sealed partial class AppOptionsRowViewModel : ObservableObject
{
    /// <summary>Checkbox text per option key — schema keys are stable frontend identifiers.</summary>
    private static readonly IReadOnlyDictionary<string, string> OptionLabels = new Dictionary<string, string>
    {
        ["ascii_mode"] = "默认英文",
        ["vim_mode"] = "Vim 模式",
        ["inline_preedit"] = "内嵌编码",
        ["inline"] = "强制内嵌编码",
        ["no_inline"] = "禁用内嵌编码",
    };

    private readonly IReadOnlyDictionary<string, object?> _unmodeled;
    private readonly HashSet<string> _loadedTrues;

    public AppOptionsRowViewModel(string appId, IReadOnlyList<string> editableKeys,
        IReadOnlyDictionary<string, object?> loaded)
    {
        _appId = appId;
        _unmodeled = loaded.Where(kv => !editableKeys.Contains(kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value);
        _loadedTrues = [.. editableKeys.Where(k => loaded.TryGetValue(k, out var v) && v is true)];
        foreach (var key in editableKeys)
        {
            var option = new AppOptionViewModel(key, OptionLabels.GetValueOrDefault(key, key), _loadedTrues.Contains(key));
            option.PropertyChanged += OnOptionChanged;
            Options.Add(option);
        }
    }

    /// <summary>Fires on any edit (including the id text) — saving collapses rows anyway.</summary>
    public event Action? Changed;

    [ObservableProperty]
    private string _appId;

    public ObservableCollection<AppOptionViewModel> Options { get; } = [];

    partial void OnAppIdChanged(string value) => Changed?.Invoke();

    /// <summary>卡片说明行：已开启选项的摘要（无勾选时「未设置输入规则」）。</summary>
    public string SummaryText
    {
        get
        {
            var on = Options.Where(o => o.IsChecked).Select(o => o.Label).ToList();
            return on.Count == 0 ? "未设置输入规则" : string.Join(" · ", on);
        }
    }

    private void OnOptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(AppOptionViewModel.IsChecked)) return;
        OnPropertyChanged(nameof(SummaryText));
        Changed?.Invoke();
    }

    /// <summary>
    /// This app's options as they should be written: unmodeled keys carried
    /// as-is, a checkbox written when checked, and written as an explicit
    /// false only when it used to be true (that false is what overrides a
    /// base-file default).
    /// </summary>
    public Dictionary<string, object?> BuildValue()
    {
        var map = new Dictionary<string, object?>(_unmodeled);
        foreach (var option in Options)
        {
            if (option.IsChecked) map[option.Key] = true;
            else if (_loadedTrues.Contains(option.Key)) map[option.Key] = false;
        }
        return map;
    }
}

/// <summary>One editable option checkbox inside an app row.</summary>
public sealed partial class AppOptionViewModel : ObservableObject
{
    public AppOptionViewModel(string key, string label, bool isChecked)
    {
        Key = key;
        Label = label;
        _isChecked = isChecked;
    }

    public string Key { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isChecked;
}
