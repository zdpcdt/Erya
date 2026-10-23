using System;
using System.Collections.Generic;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Localization;
using Erya.Services;

namespace Erya.ViewModels.Settings;

/// <summary>
/// 弹层里的一条可添加应用（正在运行的应用，Id 即 app_options 的键：
/// Windows 是 exe 名，macOS 是 Bundle ID）。
/// </summary>
public sealed record AddAppEntry(string Id, bool IsAdded, string? Title = null)
{
    /// <summary>macOS 的 Bundle ID 很长——有友好名时行首显示应用名，标识退为小字第二行。</summary>
    public bool HasTitle => Title is { Length: > 0 };
}

/// <summary>
/// 添加应用弹层（点选即添加）：正在运行的应用一个可搜索列表（Windows 按
/// exe 名、macOS 按应用名+Bundle ID 显示），点任意条目立即回调添加（默认
/// 英文）并关闭；输入无匹配时首行变「添加 “xxx”」，回车即加。已存在的
/// 条目灰显「已添加」，手输重复给行内提示——不再事后报错。
/// </summary>
public sealed partial class AddAppPickerViewModel : ObservableObject
{
    private readonly HashSet<string> _existing;
    private readonly Action<string> _add;

    public AddAppPickerViewModel(IReadOnlyCollection<string> existingIds, Action<string> add,
        IReadOnlyList<RunningApp>? runningApps = null)
    {
        _existing = new HashSet<string>(existingIds.Where(id => id.Length > 0),
            StringComparer.OrdinalIgnoreCase);
        _add = add;
        // 本机正在运行的应用（Linux 为空——那里弹层就是纯手输）。可注入供测试。
        Entries = (runningApps ?? RunningApps.Enumerate())
            .Select(app => new AddAppEntry(app.Id, IsAdded: _existing.Contains(app.Id), app.Title))
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
            if (id.Any(char.IsWhiteSpace)) return Loc.Get(LanguageManager.Instance.apps_pickerHintWhitespace);
            if (_existing.Contains(id)) return Loc.Format(LanguageManager.Instance.apps_addDuplicate, id);
            return null;
        }
    }

    /// <summary>「添加 “xxx”」行可见：输入有效、无提示、且列表里没有完全匹配。</summary>
    public bool CanAddCustom
        => SearchText.Trim().Length > 0 && HintText is null && !ExactMatchExists;

    public string CustomAddLabel => Loc.Format(LanguageManager.Instance.apps_pickerAddCustom, SearchText.Trim());

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
        // 标识或应用名命中都算——mac 用户敲的是应用名，不是 Bundle ID。
        return entries.Where(e => e.Id.Contains(query, StringComparison.OrdinalIgnoreCase)
                                  || e.Title?.Contains(query, StringComparison.OrdinalIgnoreCase) == true);
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
