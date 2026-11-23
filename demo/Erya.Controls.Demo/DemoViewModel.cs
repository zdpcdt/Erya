using System;
using System.Collections.ObjectModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using Erya.Controls;

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

    /// <summary>int 绑定：清空输入框时产生校验错误 → ValidationErrorIcon 显示感叹号。</summary>
    [ObservableProperty]
    private int _pageSize = 5;

    /// <summary>SearchablePicker 基础形态假数据：纯搜索点选（手输新建关）。</summary>
    public IReadOnlyList<PickerEntry> BasicEntries { get; } =
    [
        new PickerEntry("chrome.exe"),
        new PickerEntry("cmd.exe"),
        new PickerEntry("com.apple.Terminal", "终端"),
        new PickerEntry("com.googlecode.iterm2", "iTerm2")
    ];

    [ObservableProperty]
    private string _basicPicked = "（点上面条目试试）";

    /// <summary>完整形态假数据：混 Windows exe 名与 macOS 应用名+Bundle ID 两种形态。</summary>
    private static readonly (string Id, string? Title)[] FakeApps =
    [
        ("chrome.exe", null),
        ("cmd.exe", null),
        ("rider64.exe", null),
        ("com.apple.Terminal", "终端"),
        ("com.jetbrains.Rider", "Rider")
    ];

    /// <summary>已添加集合：既是查重集（ExistingIds），也驱动条目的「已添加」标记。</summary>
    public ObservableCollection<string> AddedApps { get; } = ["cmd.exe"];

    /// <summary>完整形态候选（AddedApps 变化时重建，刷新 IsAdded）。</summary>
    public IReadOnlyList<PickerEntry> FullEntries { get; private set; } = [];

    [ObservableProperty]
    private string _fullPicked = "";

    /// <summary>手输校验：夹空白加不了（真实控件场景里文案来自 i18n）。</summary>
    public Func<string, string?> PickerValidator =>
        static t => t.Any(char.IsWhiteSpace) ? "标识不能包含空白字符。" : null;

    /// <summary>点选/回车回调（MainWindow code-behind 转发）：入列并刷新候选。</summary>
    public void Pick(string id)
    {
        if (!AddedApps.Any(a => string.Equals(a, id, StringComparison.OrdinalIgnoreCase)))
            AddedApps.Add(id);
    }

    public DemoViewModel()
    {
        ReorderableItems.CollectionChanged += (_, _) =>
            ReorderSummary = "当前顺序：" + string.Join(" → ", ReorderableItems);
        ReorderSummary = "当前顺序：" + string.Join(" → ", ReorderableItems);

        RebuildFullEntries();
        AddedApps.CollectionChanged += (_, _) => RebuildFullEntries();
    }

    private void RebuildFullEntries()
    {
        FullEntries = FakeApps
            .Select(a => new PickerEntry(a.Id, a.Title,
                IsAdded: AddedApps.Any(x => string.Equals(x, a.Id, StringComparison.OrdinalIgnoreCase))))
            .ToList();
        OnPropertyChanged(nameof(FullEntries));
        FullPicked = AddedApps.Count > 0
            ? "已添加：" + string.Join("、", AddedApps)
            : "（还没有添加任何应用）";
    }
}
