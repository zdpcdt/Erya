using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya.Controls;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// SearchablePicker（可搜索选择面板）：条目渲染为 ListBoxItem 容器（ComboBox 下拉
/// 同款交互），选中即提交 EntryPicked；开启 AllowCustomEntry 后手输回车 /
/// 「添加 “xxx”」行走 CustomEntryAdded，重复与校验不过给行内提示、加不了。
/// </summary>
public sealed class SearchablePickerTests
{
    /// <summary>基础条目：混一条带友好名的 mac 条目覆盖两种渲染形态，cmd.exe 已添加。</summary>
    private static List<PickerEntry> Entries() =>
    [
        new PickerEntry("cmd.exe", IsAdded: true),
        new PickerEntry("chrome.exe"),
        new PickerEntry("com.apple.Terminal", "终端")
    ];

    private static (SearchablePicker Picker, Window Window) Show(
        List<PickerEntry>? items = null, Action<SearchablePicker>? configure = null)
    {
        var picker = new SearchablePicker
        {
            Width = 340,
            PlaceholderText = "搜索或输入应用标识…",
            SectionHeader = "正在运行",
            AddedText = "已添加",
            ItemsSource = items ?? Entries()
        };
        configure?.Invoke(picker);
        var window = new Window { Width = 420, Height = 640, Content = picker };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (picker, window);
    }

    /// <summary>完整形态（AllowCustomEntry + 查重集 + 文案 + 夹空白校验）。</summary>
    private static (SearchablePicker Picker, Window Window) ShowFull(
        List<PickerEntry>? items = null, List<string>? existingIds = null)
    {
        return Show(items, p =>
        {
            p.AllowCustomEntry = true;
            p.ExistingIds = existingIds ?? ["cmd.exe"];
            p.AddCustomFormat = "添加 “{0}”";
            p.DuplicateHintFormat = "已有 {0} 的规则。";
            // 夹空白校验（生产场景文案来自 i18n，由宿主注入）。
            p.ValidateCustomEntry = static t =>
                t.Any(char.IsWhiteSpace) ? "标识不能包含空白字符。" : null;
        });
    }

    private static List<ListBoxItem> EntryItems(SearchablePicker picker) =>
        picker.GetVisualDescendants().OfType<ListBoxItem>()
            .Where(i => i.DataContext is PickerEntry).ToList();

    /// <summary>添加行按钮（TextBox 的 ClearButton 内嵌按钮同在视觉树，按模板名定位）。</summary>
    private static Button AddCustomButton(SearchablePicker picker) =>
        picker.GetVisualDescendants().OfType<Button>()
            .Single(b => b.Name == SearchablePicker.PART_AddCustomButton);

    private static List<string?> VisibleTexts(Visual visual) =>
        visual.GetVisualDescendants().OfType<TextBlock>()
            .Where(t => t.GetSelfAndVisualAncestors().All(a => a.IsVisible))
            .Select(t => t.Text).ToList();

    [AvaloniaFact]
    public void Entries_render_as_listboxitems_with_added_state()
    {
        var (picker, window) = Show();

        // 条目容器是 ListBoxItem（非 Button）：三个条目各一个，已添加的禁用。
        var items = EntryItems(picker);
        Assert.Equal(["cmd.exe", "chrome.exe", "com.apple.Terminal"],
            items.Select(i => ((PickerEntry)i.DataContext!).Id));
        var disabled = items.Single(i => !i.IsEnabled);
        Assert.Equal("cmd.exe", ((PickerEntry)disabled.DataContext!).Id);
        Assert.All(items.Where(i => !ReferenceEquals(i, disabled)), i => Assert.True(i.IsEnabled));

        // 「已添加」pill 只在禁用条目里实际可见（模板里每条都带、只是折叠）。
        Assert.Contains(VisibleTexts(disabled), t => t == "已添加");

        // 带友好名的条目（macOS 形态）：应用名做主行，Bundle ID 退为小字第二行；
        // 无友好名的（Windows）单行等宽 Id。pill 折叠，不掺进可见文本。
        var macItem = items.Single(i => ((PickerEntry)i.DataContext!).HasTitle);
        Assert.Equal(["终端", "com.apple.Terminal"], VisibleTexts(macItem));
        var plainItem = items.Single(i => ((PickerEntry)i.DataContext!).Id == "chrome.exe");
        Assert.Equal(["chrome.exe"], VisibleTexts(plainItem));
        window.Close();
    }

    [AvaloniaFact]
    public void Text_filters_by_id_and_title_and_clearing_restores()
    {
        var (picker, window) = Show();

        // 标识或友好名命中都算（mac 用户敲应用名，不是 Bundle ID）。
        picker.Text = "chrome";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["chrome.exe"], picker.FilteredEntries.Select(e => e.Id));
        picker.Text = "终端";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["com.apple.Terminal"], picker.FilteredEntries.Select(e => e.Id));

        // 全滤掉：列表区还在（HasEntries）但无候选、组头藏起；清空复原。
        picker.Text = "zzz";
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(picker.FilteredEntries);
        Assert.True(picker.HasEntries);
        Assert.False(picker.HasFilteredEntries);
        picker.Text = "";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(3, picker.FilteredEntries.Count);
        window.Close();
    }

    [AvaloniaFact]
    public void Pointer_click_on_entry_commits_pick_and_resets_selection()
    {
        var (picker, window) = Show();
        var picked = new List<string>();
        picker.EntryPicked += (_, e) => picked.Add(e.Entry.Id);

        // 真实指针路径点条目：选中即提交（入列的是业务键 Id，不是友好名）。
        var target = EntryItems(picker).Single(i => ((PickerEntry)i.DataContext!).Id == "chrome.exe");
        var center = target.TranslatePoint(
            new Point(target.Bounds.Width / 2, target.Bounds.Height / 2), window)!.Value;
        window.MouseMove(center);
        window.MouseDown(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
        window.MouseUp(center, MouseButton.Left);
        Dispatcher.UIThread.RunJobs();

        Assert.Equal(["chrome.exe"], picked);
        // 无持久选中态（ComboBox 提交后复位）。
        Assert.Null(picker.GetVisualDescendants().OfType<ListBox>().Single().SelectedItem);
        window.Close();
    }

    [AvaloniaFact]
    public void Selecting_added_entry_does_not_raise()
    {
        var (picker, window) = Show();
        var picked = new List<string>();
        picker.EntryPicked += (_, e) => picked.Add(e.Entry.Id);

        var list = picker.GetVisualDescendants().OfType<ListBox>().Single();
        list.SelectedItem = list.ItemsSource!.OfType<PickerEntry>().Single(e => e.Id == "cmd.exe");
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(picked);

        // 提交后选中态复位，同一目标可再次选中再次提交。
        list.SelectedItem = list.ItemsSource!.OfType<PickerEntry>().Single(e => e.Id == "com.apple.Terminal");
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["com.apple.Terminal"], picked);
        Assert.Null(list.SelectedItem);
        window.Close();
    }

    [AvaloniaFact]
    public void ItemTemplate_can_be_replaced()
    {
        var (picker, window) = Show(configure: p => p.ItemTemplate = new FuncDataTemplate<PickerEntry>(
            (entry, _) => new TextBlock { Text = "T:" + entry.Id }));

        var texts = EntryItems(picker)
            .SelectMany(VisibleTexts).ToList();
        Assert.Equal(["T:cmd.exe", "T:chrome.exe", "T:com.apple.Terminal"], texts);
        window.Close();
    }

    [AvaloniaFact]
    public void Custom_entry_row_shows_for_new_id_and_enter_adds()
    {
        var (picker, window) = ShowFull();
        var added = new List<string>();
        picker.CustomEntryAdded += (_, e) => added.Add(e.Id);

        // 全新标识 → 首行「添加 “xxx”」，回车/点击即加。
        picker.Text = "myapp.exe";
        Dispatcher.UIThread.RunJobs();
        Assert.True(picker.CanAddCustom);
        Assert.Null(picker.HintText);
        var button = AddCustomButton(picker);
        Assert.True(button.IsVisible);
        Assert.Equal("添加 “myapp.exe”",
            button.GetVisualDescendants().OfType<TextBlock>().Single().Text);

        var search = picker.GetVisualDescendants().OfType<TextBox>().Single();
        search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.Equal(["myapp.exe"], added);
        window.Close();
    }

    [AvaloniaFact]
    public void Duplicate_and_whitespace_show_inline_hints_and_block_add()
    {
        var (picker, window) = ShowFull();
        var added = new List<string>();
        picker.CustomEntryAdded += (_, e) => added.Add(e.Id);

        // 已有规则的标识 → 行内提示、无添加行（不再事后报错）。
        picker.Text = "cmd.exe";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("已有 cmd.exe 的规则。", picker.HintText);
        Assert.False(picker.CanAddCustom);
        Assert.Contains(VisibleTexts(picker), t => t == "已有 cmd.exe 的规则。");

        // 夹空白 → 宿主校验的行内提示，回车加不了。
        picker.Text = "pu tty.exe";
        Dispatcher.UIThread.RunJobs();
        Assert.Equal("标识不能包含空白字符。", picker.HintText);
        Assert.False(picker.CanAddCustom);
        Assert.False(AddCustomButton(picker).IsVisible);

        var search = picker.GetVisualDescendants().OfType<TextBox>().Single();
        search.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Dispatcher.UIThread.RunJobs();
        Assert.Empty(added);
        window.Close();
    }

    [AvaloniaFact]
    public void Exact_match_in_entries_hides_add_row()
    {
        // 手输恰好等于列表条目的标识：点那条即可，不出「添加」行；前缀不算完全匹配。
        var (picker, window) = ShowFull();
        picker.Text = "chrome.exe";
        Dispatcher.UIThread.RunJobs();
        Assert.True(picker.ExactMatchExists);
        Assert.False(picker.CanAddCustom);
        Assert.Null(picker.HintText);

        picker.Text = "chrome";
        Dispatcher.UIThread.RunJobs();
        Assert.False(picker.ExactMatchExists);
        Assert.True(picker.CanAddCustom);
        window.Close();
    }

    [AvaloniaFact]
    public void AllowCustomEntry_false_hides_tri_state_row_even_for_duplicate()
    {
        // 扩展关闭时是纯搜索点选面板：无添加行、无提示（查重文案等只在开启时使用）。
        var (picker, window) = Show(configure: p => p.ExistingIds = ["cmd.exe"]);
        picker.Text = "cmd.exe";
        Dispatcher.UIThread.RunJobs();
        Assert.False(picker.CanAddCustom);
        Assert.Null(picker.HintText);
        Assert.False(AddCustomButton(picker).IsVisible);
        window.Close();
    }
}
