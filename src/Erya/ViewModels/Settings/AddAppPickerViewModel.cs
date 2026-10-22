using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Erya.ViewModels.Settings;

/// <summary>弹层里的一条可添加应用：预设条目带友好名（副行显示标识），运行中条目标识即标题。</summary>
public sealed record AddAppEntry(string Id, string Title, bool IsAdded)
{
    /// <summary>副行是否显示标识（预设 Title≠Id 才显示，运行中的单行）。</summary>
    public bool ShowId => !string.Equals(Title, Id, StringComparison.Ordinal);
}

/// <summary>
/// 添加应用弹层（点选即添加）：常用应用 + 正在运行的应用合并为一个可搜索列表，
/// 点任意条目立即回调添加（默认英文）并关闭；输入无匹配时首行变「添加 “xxx”」，
/// 回车即加。已存在的条目灰显「已添加」，手输重复给行内提示——不再事后报错。
/// </summary>
public sealed partial class AddAppPickerViewModel : ObservableObject
{
    private readonly HashSet<string> _existing;
    private readonly Action<string> _add;

    public AddAppPickerViewModel(IReadOnlyCollection<string> existingIds, Action<string> add,
        IReadOnlyList<AppPresetItem>? presets = null)
    {
        _existing = new HashSet<string>(existingIds.Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        _add = add;
        // 预设按平台给出（Linux 上外观页整组不出现）；可注入供测试在任何平台跑。
        var presetList = presets ?? AppBehaviorPresets.ForCurrentOs();
        PresetEntries = presetList
            .Select(p => new AddAppEntry(p.Id, p.Title, IsAdded: _existing.Contains(p.Id)))
            .ToList();
        // 运行中（仅 Windows）：与常用区重合的不重复列出；进程 exe 名即标识。
        RunningEntries = OperatingSystem.IsWindows()
            ? AppBehaviorPresets.RunningWindowsApps()
                .Where(id => !presetList.Any(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)))
                .Select(id => new AddAppEntry(id, id, IsAdded: _existing.Contains(id)))
                .ToList()
            : [];
    }

    public IReadOnlyList<AddAppEntry> PresetEntries { get; }

    public IReadOnlyList<AddAppEntry> RunningEntries { get; }

    /// <summary>运行中分组仅在 Windows 有意义（macOS 进程名不是 Bundle ID）。</summary>
    public bool HasRunning => RunningEntries.Count > 0;

    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value) => Refresh();

    public IEnumerable<AddAppEntry> FilteredPresets => Filter(PresetEntries);

    public IEnumerable<AddAppEntry> FilteredRunning => Filter(RunningEntries);

    public bool HasFilteredPresets => FilteredPresets.Any();

    public bool HasFilteredRunning => FilteredRunning.Any();

    /// <summary>手输的标识恰好是列表里的某个条目——点那条即可，不出「添加」行。</summary>
    public bool ExactMatchExists
    {
        get
        {
            var query = SearchText.Trim();
            return query.Length > 0
                   && Filter(PresetEntries).Concat(Filter(RunningEntries))
                       .Any(e => string.Equals(e.Id, query, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>输入有值但加不了时的行内提示（重复 / 夹空白）；null = 无提示。</summary>
    public string? HintText
    {
        get
        {
            var id = SearchText.Trim();
            if (id.Length == 0) return null;
            if (id.Any(char.IsWhiteSpace)) return "标识不能包含空白字符。";
            if (_existing.Contains(id)) return $"已有 {id} 的规则。";
            return null;
        }
    }

    /// <summary>「添加 “xxx”」行可见：输入有效、无提示、且列表里没有完全匹配。</summary>
    public bool CanAddCustom
        => SearchText.Trim().Length > 0 && HintText is null && !ExactMatchExists;

    public string CustomAddLabel => $"添加 “{SearchText.Trim()}”";

    [RelayCommand]
    private void PickEntry(AddAppEntry? entry)
    {
        if (entry is { IsAdded: false }) _add(entry.Id);
    }

    [RelayCommand]
    private void AddCustom()
    {
        if (CanAddCustom) _add(SearchText.Trim());
    }

    private IEnumerable<AddAppEntry> Filter(IReadOnlyList<AddAppEntry> entries)
    {
        var query = SearchText.Trim();
        if (query.Length == 0) return entries;
        return entries.Where(e => e.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                                  || e.Title.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(FilteredPresets));
        OnPropertyChanged(nameof(FilteredRunning));
        OnPropertyChanged(nameof(HasFilteredPresets));
        OnPropertyChanged(nameof(HasFilteredRunning));
        OnPropertyChanged(nameof(ExactMatchExists));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(CanAddCustom));
        OnPropertyChanged(nameof(CustomAddLabel));
    }
}
