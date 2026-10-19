using System.Collections.ObjectModel;
using System.Collections.Specialized;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Erya.Controls.Demo;

/// <summary>拖拽示例的勾选行（方案管理页同款结构）。</summary>
public partial class DemoSchemaRow(string name, string id, bool isSelected) : ObservableObject
{
    public string Name { get; } = name;

    public string Id { get; } = id;

    [ObservableProperty]
    private bool _isSelected = isSelected;
}

/// <summary>Demo 页状态：与控件无耦合，仅提供可绑定数据。</summary>
public partial class DemoViewModel : ObservableObject
{
    /// <summary>拖拽排序示例的行数据；ReorderableItemsControl 直接 Move 这个集合。</summary>
    public ObservableCollection<string> ReorderableItems { get; } =
    [
        "明月拼音·简化字",
        "雾凇拼音",
        "仓颉五代",
        "双拼（小鹤）",
        "地球拼音",
        "注音"
    ];

    /// <summary>勾选行拖拽示例的数据（把手 + CheckBox 共存，方案管理页同款）。</summary>
    public ObservableCollection<DemoSchemaRow> SchemaRows { get; } =
    [
        new DemoSchemaRow("明月拼音·简化字", "luna_pinyin", true),
        new DemoSchemaRow("雾凇拼音", "rime_ice", true),
        new DemoSchemaRow("仓颉五代", "cangjie5", false),
        new DemoSchemaRow("双拼（小鹤）", "double_pinyin_flypy", false)
    ];

    [ObservableProperty]
    private string _reorderSummary = "";

    public DemoViewModel()
    {
        ReorderableItems.CollectionChanged += (_, _) =>
            ReorderSummary = "当前顺序：" + string.Join(" → ", ReorderableItems);
        ReorderSummary = "当前顺序：" + string.Join(" → ", ReorderableItems);
    }

    [ObservableProperty]
    private bool _autoCommitAscii;

    /// <summary>int 绑定：清空输入框时产生校验错误 → ValidationErrorIcon 显示感叹号。</summary>
    [ObservableProperty]
    private int _pageSize = 5;

    [ObservableProperty]
    private string _customPhrase = "/sl";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FontPointSummary))]
    private double _fontPoint = 14;

    public string FontPointSummary => $"{FontPoint:0.#} pt";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FuzzySummary))]
    private bool _fuzzyZh = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FuzzySummary))]
    private bool _fuzzyNasal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FuzzySummary))]
    private bool _fuzzyHF;

    public string FuzzySummary
    {
        get
        {
            var count = (FuzzyZh ? 1 : 0) + (FuzzyNasal ? 1 : 0) + (FuzzyHF ? 1 : 0);
            return count == 0 ? "未启用" : $"已启用 {count} 项";
        }
    }
}
