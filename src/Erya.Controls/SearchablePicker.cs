using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace Erya.Controls;

/// <summary>
/// 可搜索选择面板：候选列表（<see cref="PickerEntry"/>）按 <see cref="Text"/> 实时过滤，
/// 点条目（ListBoxItem 容器，ComboBox 下拉同款交互）立即通过 <see cref="EntryPicked"/>
/// 回调业务键。开启 <see cref="AllowCustomEntry"/> 后，输入没有完全匹配时首行变
/// 「添加 “{0}”」，回车即通过 <see cref="CustomEntryAdded"/> 回调；已有标识 / 宿主校验
/// 不过时给行内提示、加不了——不再事后报错。
/// </summary>
/// <remarks>
/// <para>
/// 弹层内容控件（挂在 Button.Flyout / Dialog / Popup 下均可），本身不管收起与写回：
/// 宿主监听两个事件，自行处理选中和关闭。数据在 <see cref="ItemsSource"/> 里物化一次，
/// 打开时整表重建即可刷新「已添加」状态。
/// </para>
/// <code>
/// &lt;c:SearchablePicker Width="340" AllowCustomEntry="True"
///     ItemsSource="{Binding Entries}" Text="{Binding SearchText, Mode=TwoWay}"
///     PlaceholderText="搜索或输入应用标识…" SectionHeader="正在运行" AddedText="已添加"
///     AddCustomFormat="添加 “{0}”" DuplicateHintFormat="已有 {0} 的规则。"
///     EntryPicked="OnEntryPicked" CustomEntryAdded="OnCustomEntryAdded" /&gt;
/// </code>
/// </remarks>
public sealed class SearchablePicker : TemplatedControl
{
    public const string PART_SearchBox = "PART_SearchBox";
    public const string PART_AddCustomButton = "PART_AddCustomButton";
    public const string PART_List = "PART_List";

    /// <summary>Identifies the <see cref="ItemsSource"/> property.</summary>
    public static readonly StyledProperty<IEnumerable<PickerEntry>?> ItemsSourceProperty =
        AvaloniaProperty.Register<SearchablePicker, IEnumerable<PickerEntry>?>(nameof(ItemsSource));

    /// <summary>Identifies the <see cref="Text"/> property.</summary>
    public static readonly StyledProperty<string?> TextProperty =
        AvaloniaProperty.Register<SearchablePicker, string?>(nameof(Text), "",
            defaultBindingMode: BindingMode.TwoWay);

    /// <summary>Identifies the <see cref="FilterFunc"/> property.</summary>
    public static readonly StyledProperty<Func<PickerEntry, string, bool>?> FilterFuncProperty =
        AvaloniaProperty.Register<SearchablePicker, Func<PickerEntry, string, bool>?>(nameof(FilterFunc));

    /// <summary>Identifies the <see cref="ItemTemplate"/> property.</summary>
    public static readonly StyledProperty<IDataTemplate?> ItemTemplateProperty =
        AvaloniaProperty.Register<SearchablePicker, IDataTemplate?>(nameof(ItemTemplate));

    /// <summary>Identifies the <see cref="PlaceholderText"/> property.</summary>
    public static readonly StyledProperty<string?> PlaceholderTextProperty =
        TextBox.PlaceholderTextProperty.AddOwner<SearchablePicker>();

    /// <summary>Identifies the <see cref="SectionHeader"/> property.</summary>
    public static readonly StyledProperty<string?> SectionHeaderProperty =
        AvaloniaProperty.Register<SearchablePicker, string?>(nameof(SectionHeader));

    /// <summary>Identifies the <see cref="AddedText"/> property.</summary>
    public static readonly StyledProperty<string?> AddedTextProperty =
        AvaloniaProperty.Register<SearchablePicker, string?>(nameof(AddedText));

    /// <summary>Identifies the <see cref="MaxListHeight"/> property.</summary>
    public static readonly StyledProperty<double> MaxListHeightProperty =
        AvaloniaProperty.Register<SearchablePicker, double>(nameof(MaxListHeight), 380);

    /// <summary>Identifies the <see cref="AllowCustomEntry"/> property.</summary>
    public static readonly StyledProperty<bool> AllowCustomEntryProperty =
        AvaloniaProperty.Register<SearchablePicker, bool>(nameof(AllowCustomEntry));

    /// <summary>Identifies the <see cref="ExistingIds"/> property.</summary>
    public static readonly StyledProperty<IEnumerable<string>?> ExistingIdsProperty =
        AvaloniaProperty.Register<SearchablePicker, IEnumerable<string>?>(nameof(ExistingIds));

    /// <summary>Identifies the <see cref="AddCustomFormat"/> property.</summary>
    public static readonly StyledProperty<string?> AddCustomFormatProperty =
        AvaloniaProperty.Register<SearchablePicker, string?>(nameof(AddCustomFormat));

    /// <summary>Identifies the <see cref="DuplicateHintFormat"/> property.</summary>
    public static readonly StyledProperty<string?> DuplicateHintFormatProperty =
        AvaloniaProperty.Register<SearchablePicker, string?>(nameof(DuplicateHintFormat));

    /// <summary>Identifies the <see cref="ValidateCustomEntry"/> property.</summary>
    public static readonly StyledProperty<Func<string, string?>?> ValidateCustomEntryProperty =
        AvaloniaProperty.Register<SearchablePicker, Func<string, string?>?>(nameof(ValidateCustomEntry));

    /// <summary>Identifies the <see cref="FilteredEntries"/> property.</summary>
    public static readonly DirectProperty<SearchablePicker, IReadOnlyList<PickerEntry>> FilteredEntriesProperty =
        AvaloniaProperty.RegisterDirect<SearchablePicker, IReadOnlyList<PickerEntry>>(
            nameof(FilteredEntries), o => o._filteredEntries);

    /// <summary>Identifies the <see cref="HasEntries"/> property.</summary>
    public static readonly DirectProperty<SearchablePicker, bool> HasEntriesProperty =
        AvaloniaProperty.RegisterDirect<SearchablePicker, bool>(nameof(HasEntries), o => o._hasEntries);

    /// <summary>Identifies the <see cref="HasFilteredEntries"/> property.</summary>
    public static readonly DirectProperty<SearchablePicker, bool> HasFilteredEntriesProperty =
        AvaloniaProperty.RegisterDirect<SearchablePicker, bool>(nameof(HasFilteredEntries), o => o._hasFilteredEntries);

    /// <summary>Identifies the <see cref="ExactMatchExists"/> property.</summary>
    public static readonly DirectProperty<SearchablePicker, bool> ExactMatchExistsProperty =
        AvaloniaProperty.RegisterDirect<SearchablePicker, bool>(nameof(ExactMatchExists), o => o._exactMatchExists);

    /// <summary>Identifies the <see cref="HintText"/> property.</summary>
    public static readonly DirectProperty<SearchablePicker, string?> HintTextProperty =
        AvaloniaProperty.RegisterDirect<SearchablePicker, string?>(nameof(HintText), o => o._hintText);

    /// <summary>Identifies the <see cref="CanAddCustom"/> property.</summary>
    public static readonly DirectProperty<SearchablePicker, bool> CanAddCustomProperty =
        AvaloniaProperty.RegisterDirect<SearchablePicker, bool>(nameof(CanAddCustom), o => o._canAddCustom);

    /// <summary>Identifies the <see cref="AddCustomLabel"/> property.</summary>
    public static readonly DirectProperty<SearchablePicker, string?> AddCustomLabelProperty =
        AvaloniaProperty.RegisterDirect<SearchablePicker, string?>(nameof(AddCustomLabel), o => o._addCustomLabel);

    /// <summary>Picked on entry selection（点条目即添加）。</summary>
    public static readonly RoutedEvent<EntryPickedEventArgs> EntryPickedEvent =
        RoutedEvent.Register<SearchablePicker, EntryPickedEventArgs>(nameof(EntryPicked), RoutingStrategies.Bubble);

    /// <summary>Custom entry added（回车 / 点「添加 “xxx”” 行）。</summary>
    public static readonly RoutedEvent<CustomEntryAddedEventArgs> CustomEntryAddedEvent =
        RoutedEvent.Register<SearchablePicker, CustomEntryAddedEventArgs>(
            nameof(CustomEntryAdded), RoutingStrategies.Bubble);

    /// <summary>点条目即添加（入参是业务键 Id 所在的条目）。</summary>
    public event EventHandler<EntryPickedEventArgs>? EntryPicked
    {
        add => AddHandler(EntryPickedEvent, value);
        remove => RemoveHandler(EntryPickedEvent, value);
    }

    /// <summary>手输标识确认添加（已是 Trim 后的值）。</summary>
    public event EventHandler<CustomEntryAddedEventArgs>? CustomEntryAdded
    {
        add => AddHandler(CustomEntryAddedEvent, value);
        remove => RemoveHandler(CustomEntryAddedEvent, value);
    }

    private List<PickerEntry> _entries = [];
    private HashSet<string>? _existingSet;
    private IReadOnlyList<PickerEntry> _filteredEntries = [];
    private bool _hasEntries;
    private bool _hasFilteredEntries;
    private bool _exactMatchExists;
    private string? _hintText;
    private bool _canAddCustom;
    private string? _addCustomLabel;

    private TextBox? _searchBox;
    private Button? _addCustomButton;
    private ListBox? _list;

    /// <summary>候选列表（打开时整表重建，物化一次；运行中更新集合不生效）。</summary>
    public IEnumerable<PickerEntry>? ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>搜索/手输合一的输入值（两路绑定；过滤与三态判定都用 Trim 后的值）。</summary>
    public string? Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    /// <summary>自定义过滤谓词（入参 = 条目与 Trim 后的查询）；缺省为 Id / 友好名包含匹配（大小写不敏感）。</summary>
    public Func<PickerEntry, string, bool>? FilterFunc
    {
        get => GetValue(FilterFuncProperty);
        set => SetValue(FilterFuncProperty, value);
    }

    /// <summary>条目内容模板；默认渲染「单行 Id / 应用名+小字 Id」双形态与「已添加」pill。</summary>
    public IDataTemplate? ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    /// <summary>搜索框占位文本。</summary>
    public string? PlaceholderText
    {
        get => GetValue(PlaceholderTextProperty);
        set => SetValue(PlaceholderTextProperty, value);
    }

    /// <summary>候选列表组头（应用场景为「正在运行」）。</summary>
    public string? SectionHeader
    {
        get => GetValue(SectionHeaderProperty);
        set => SetValue(SectionHeaderProperty, value);
    }

    /// <summary>「已添加」pill 文案。</summary>
    public string? AddedText
    {
        get => GetValue(AddedTextProperty);
        set => SetValue(AddedTextProperty, value);
    }

    /// <summary>候选列表最大高度（内部滚动）。</summary>
    public double MaxListHeight
    {
        get => GetValue(MaxListHeightProperty);
        set => SetValue(MaxListHeightProperty, value);
    }

    /// <summary>是否启用手输新建（「添加 “xxx”” 行 + 行内提示）。关闭时是纯搜索点选面板。</summary>
    public bool AllowCustomEntry
    {
        get => GetValue(AllowCustomEntryProperty);
        set => SetValue(AllowCustomEntryProperty, value);
    }

    /// <summary>已存在的业务键（大小写不敏感查重，独立于 <see cref="ItemsSource"/>——无候选时也要能查重）。</summary>
    public IEnumerable<string>? ExistingIds
    {
        get => GetValue(ExistingIdsProperty);
        set => SetValue(ExistingIdsProperty, value);
    }

    /// <summary>「添加 “{0}”」行文案模板。</summary>
    public string? AddCustomFormat
    {
        get => GetValue(AddCustomFormatProperty);
        set => SetValue(AddCustomFormatProperty, value);
    }

    /// <summary>重复提示模板（如「已有 {0} 的规则。」）。</summary>
    public string? DuplicateHintFormat
    {
        get => GetValue(DuplicateHintFormatProperty);
        set => SetValue(DuplicateHintFormatProperty, value);
    }

    /// <summary>宿主校验（返回错误文案即显示行内提示并禁止添加；null = 通过）。先于查重执行。</summary>
    public Func<string, string?>? ValidateCustomEntry
    {
        get => GetValue(ValidateCustomEntryProperty);
        set => SetValue(ValidateCustomEntryProperty, value);
    }

    /// <summary>按 <see cref="Text"/> 过滤后的候选。</summary>
    public IReadOnlyList<PickerEntry> FilteredEntries => _filteredEntries;

    /// <summary>有候选（决定列表区是否出现）。</summary>
    public bool HasEntries => _hasEntries;

    /// <summary>过滤后仍有候选（决定组头是否出现）。</summary>
    public bool HasFilteredEntries => _hasFilteredEntries;

    /// <summary>手输的标识恰好是列表里的某个条目——点那条即可，不出「添加」行。</summary>
    public bool ExactMatchExists => _exactMatchExists;

    /// <summary>输入有值但加不了时的行内提示（宿主校验 / 重复）；null = 无提示。</summary>
    public string? HintText => _hintText;

    /// <summary>「添加 “xxx”」行可见：扩展开启、输入有效、无提示、且列表里没有完全匹配。</summary>
    public bool CanAddCustom => _canAddCustom;

    /// <summary>「添加 “xxx”」行文案（含当前输入）。</summary>
    public string? AddCustomLabel => _addCustomLabel;

    /// <summary>Flyout 每次打开都会重新挂树——顺带把焦点放进搜索框。</summary>
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Dispatcher.UIThread.Post(() => _searchBox?.Focus(), DispatcherPriority.Input);
    }

    /// <summary>挂模板部件：搜索框回车、添加行点击、列表选中即提交。</summary>
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        if (_searchBox is { } oldBox) oldBox.KeyDown -= OnSearchKeyDown;
        if (_addCustomButton is { } oldButton) oldButton.Click -= OnAddCustomClick;
        if (_list is { } oldList) oldList.SelectionChanged -= OnListSelectionChanged;

        _searchBox = e.NameScope.Find<TextBox>(PART_SearchBox);
        _addCustomButton = e.NameScope.Find<Button>(PART_AddCustomButton);
        _list = e.NameScope.Find<ListBox>(PART_List);

        if (_searchBox is { } box) box.KeyDown += OnSearchKeyDown;
        if (_addCustomButton is { } button) button.Click += OnAddCustomClick;
        if (_list is { } list) list.SelectionChanged += OnListSelectionChanged;
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.Property == ItemsSourceProperty)
        {
            _entries = (e.GetNewValue<IEnumerable<PickerEntry>?>() ?? []).ToList();
        }
        else if (e.Property == ExistingIdsProperty)
        {
            var ids = e.GetNewValue<IEnumerable<string>?>();
            _existingSet = ids is null
                ? null
                : new HashSet<string>(ids.Where(id => id.Length > 0), StringComparer.OrdinalIgnoreCase);
        }

        if (e.Property == ItemsSourceProperty
            || e.Property == TextProperty
            || e.Property == FilterFuncProperty
            || e.Property == AllowCustomEntryProperty
            || e.Property == ExistingIdsProperty
            || e.Property == ValidateCustomEntryProperty
            || e.Property == AddCustomFormatProperty
            || e.Property == DuplicateHintFormatProperty)
        {
            Refresh();
        }
    }

    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || !CanAddCustom) return;
        e.Handled = true;
        TryAddCustom();
    }

    private void OnAddCustomClick(object? sender, RoutedEventArgs e) => TryAddCustom();

    /// <summary>选中即提交（ComboBox 下拉模式）：取选中的条目、复位选中、回调业务键。</summary>
    private void OnListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        // 复位（SelectedItem=null）引发的空变更直接忽略。
        if (_list is null || e.AddedItems.Count == 0) return;
        var entry = e.AddedItems.OfType<PickerEntry>().FirstOrDefault();
        _list.SelectedItem = null;
        if (entry is { IsAdded: false })
            RaiseEvent(new EntryPickedEventArgs(EntryPickedEvent, entry));
    }

    private void TryAddCustom()
    {
        if (!CanAddCustom) return;
        RaiseEvent(new CustomEntryAddedEventArgs(CustomEntryAddedEvent, (Text ?? "").Trim()));
    }

    private void Refresh()
    {
        var query = (Text ?? "").Trim();

        var filtered = query.Length == 0
            ? _entries
            : _entries.Where(e => Matches(e, query)).ToList();
        SetAndRaise(FilteredEntriesProperty, ref _filteredEntries, filtered);
        SetAndRaise(HasEntriesProperty, ref _hasEntries, _entries.Count > 0);
        SetAndRaise(HasFilteredEntriesProperty, ref _hasFilteredEntries, filtered.Count > 0);

        var exactMatch = query.Length > 0 && _entries.Any(e =>
            string.Equals(e.Id, query, StringComparison.OrdinalIgnoreCase));
        SetAndRaise(ExactMatchExistsProperty, ref _exactMatchExists, exactMatch);

        var hint = ComputeHint(query);
        SetAndRaise(HintTextProperty, ref _hintText, hint);

        var canAdd = AllowCustomEntry && query.Length > 0 && hint is null && !exactMatch;
        SetAndRaise(CanAddCustomProperty, ref _canAddCustom, canAdd);
        SetAndRaise(AddCustomLabelProperty, ref _addCustomLabel, canAdd ? Format(AddCustomFormat, query) : null);
    }

    /// <summary>标识或友好名命中都算——mac 用户敲的是应用名，不是 Bundle ID。</summary>
    private bool Matches(PickerEntry entry, string query) =>
        FilterFunc?.Invoke(entry, query)
        ?? (entry.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
            || entry.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);

    private string? ComputeHint(string id)
    {
        if (!AllowCustomEntry || id.Length == 0) return null;
        if (ValidateCustomEntry?.Invoke(id) is { } error) return error;
        if (_existingSet?.Contains(id) == true) return Format(DuplicateHintFormat, id);
        return null;
    }

    private static string? Format(string? format, string arg) =>
        string.IsNullOrEmpty(format) ? null : string.Format(format, arg);
}

/// <summary><see cref="SearchablePicker.EntryPicked"/> 事件参数。</summary>
public sealed class EntryPickedEventArgs(RoutedEvent routedEvent, PickerEntry entry) : RoutedEventArgs(routedEvent)
{
    /// <summary>被选中的条目（业务键即 <see cref="PickerEntry.Id"/>）。</summary>
    public PickerEntry Entry { get; } = entry;
}

/// <summary><see cref="SearchablePicker.CustomEntryAdded"/> 事件参数。</summary>
public sealed class CustomEntryAddedEventArgs(RoutedEvent routedEvent, string id) : RoutedEventArgs(routedEvent)
{
    /// <summary>手输的业务键（Trim 后）。</summary>
    public string Id { get; } = id;
}
