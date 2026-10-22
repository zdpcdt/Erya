using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Erya.ViewModels.Settings;

/// <summary>弹层里的一条可添加应用（正在运行的进程，exe 名即标识）。</summary>
public sealed record AddAppEntry(string Id, bool IsAdded);

/// <summary>
/// 添加应用弹层（点选即添加）：正在运行的应用一个可搜索列表，点任意条目
/// 立即回调添加（默认英文）并关闭；输入无匹配时首行变「添加 “xxx”」，
/// 回车即加。已存在的条目灰显「已添加」，手输重复给行内提示——不再事后报错。
/// </summary>
public sealed partial class AddAppPickerViewModel : ObservableObject
{
    private readonly HashSet<string> _existing;
    private readonly Action<string> _add;

    public AddAppPickerViewModel(IReadOnlyCollection<string> existingIds, Action<string> add,
        IReadOnlyList<string>? runningApps = null)
    {
        _existing = new HashSet<string>(existingIds.Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        _add = add;
        // 运行中（仅 Windows 有意义：进程 exe 名才是 app_options 的键；
        // macOS 进程名≠Bundle ID，为空——那里弹层就是纯手输）。可注入供测试。
        Entries = (runningApps ?? AppBehaviorPresets.RunningWindowsApps())
            .Select(id => new AddAppEntry(id, IsAdded: _existing.Contains(id)))
            .ToList();
    }

    public IReadOnlyList<AddAppEntry> Entries { get; }

    public bool HasEntries => Entries.Count > 0;

    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value) => Refresh();

    public IEnumerable<AddAppEntry> FilteredEntries => Filter(Entries);

    public bool HasFilteredEntries => FilteredEntries.Any();

    /// <summary>手输的标识恰好是列表里的某个条目——点那条即可，不出「添加」行。</summary>
    public bool ExactMatchExists
    {
        get
        {
            var query = SearchText.Trim();
            return query.Length > 0
                   && Filter(Entries).Any(e => string.Equals(e.Id, query, StringComparison.OrdinalIgnoreCase));
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
        return entries.Where(e => e.Id.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void Refresh()
    {
        OnPropertyChanged(nameof(FilteredEntries));
        OnPropertyChanged(nameof(HasFilteredEntries));
        OnPropertyChanged(nameof(ExactMatchExists));
        OnPropertyChanged(nameof(HintText));
        OnPropertyChanged(nameof(CanAddCustom));
        OnPropertyChanged(nameof(CustomAddLabel));
    }
}
