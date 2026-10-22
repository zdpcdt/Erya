using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;
using Erya.Services;
using Erya.ViewModels.Advanced;
using Erya.ViewModels.Settings;

namespace Erya.ViewModels;

/// <summary>Sidebar entry; selection is driven by NavMenu's TwoWay SelectedItem binding.
/// Entries with <see cref="Children"/> are groups: clicking only expands the submenu,
/// the page lives on the leaves.</summary>
public sealed record NavItem(string Key, string Header, Geometry? Icon, IReadOnlyList<NavItem>? Children = null)
{
    // Fallback display (container renders ToString) should never leak internals.
    public override string ToString() => Header;
}

public partial class ShellViewModel : ObservableObject
{
    private readonly MainViewModel _overview;
    // 外观三页编辑的是前端自带的 UI YAML（squirrel/weasel）；Linux 前端
    // （fcitx5/ibus）的候选面板由输入法框架自己绘制，没有这份 YAML，
    // 组合根（App.axaml.cs）在 Linux 上关闭整组入口。
    private readonly bool _showAppearance;
    private SchemaManagerViewModel _schemas = null!;
    private SettingsEditorViewModel _inputSettings = null!;
    private SettingsEditorViewModel _appearance = null!;
    private SkinPageViewModel _skin = null!;
    private LayoutPageViewModel _layout = null!;
    private AppsPageViewModel _apps = null!;
    private AdvancedSettingsViewModel _advanced = null!;
    private BackupRestoreViewModel _backup = null!;
    private Dictionary<string, (bool Loaded, ObservableObject Page)> _lazyPages = [];
    private IRimeDistribution? _distribution;

    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    private NavItem? _selectedNav;

    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private string _pageTitle = "";

    /// <summary>Global redeploy action — bound to Ctrl+R / Cmd+R in the window.</summary>
    [RelayCommand]
    private void Deploy() => _overview.DeployCommand.Execute(null);

    public ShellViewModel(DistributionManager distributions, AppPreferences? preferences = null,
        bool showAppearance = true)
    {
        _showAppearance = showAppearance;
        _overview = new MainViewModel(distributions, preferences);
        BuildPages();
        // A frontend switch rebuilds every page against the new distribution;
        // unsaved edits on other pages are deliberately dropped (rare action).
        _overview.DistributionChanged += RebuildPages;

        List<NavItem> items =
        [
            new NavItem("overview", "概览", Icon("SemiIconHome")),
            new NavItem("schemas", "方案管理", Icon("SemiIconCheckList")),
            new NavItem("inputSettings", "输入设置", Icon("SemiIconSetting")),
        ];
        if (_showAppearance)
        {
            items.Add(new NavItem("appearance", "外观设置", Icon("SemiIconColorPalette"),
            [
                new NavItem("skin", "皮肤库", Icon("SemiIconColorPalette")),
                new NavItem("layout", "候选窗排版", Icon("SemiIconSetting")),
                new NavItem("apps", "应用行为", Icon("SemiIconDesktop")),
            ]));
        }
        items.Add(new NavItem("advanced", "高级设置", Icon("SemiIconCode")));
        items.Add(new NavItem("backup", "备份恢复", Icon("SemiIconRestore")));
        NavItems = items;
        InitializeLazyPages();

        // Two-way binding pushes this into NavMenu, selecting the item visually.
        _selectedNav = NavItems[0];
        CurrentPage = _overview;
        PageTitle = _selectedNav.Header;
    }

    private void BuildPages()
    {
        _distribution = _overview.Distribution;
        _schemas = new SchemaManagerViewModel(_distribution);
        _inputSettings = new SettingsEditorViewModel(_distribution, "default");
        if (_showAppearance)
        {
            // 外观三个子页共享同一个编辑器：一份字段/皮肤库/预览/保存动作。
            _appearance = new SettingsEditorViewModel(_distribution, "ui");
            _skin = new SkinPageViewModel(_appearance);
            _layout = new LayoutPageViewModel(_appearance);
            _apps = new AppsPageViewModel(_appearance);
        }
        _advanced = new AdvancedSettingsViewModel(_distribution);
        _backup = new BackupRestoreViewModel(_distribution);
    }

    private void InitializeLazyPages()
    {
        _lazyPages = [];
        _lazyPages["schemas"] = (false, _schemas);
        _lazyPages["inputSettings"] = (false, _inputSettings);
        if (_showAppearance)
        {
            _lazyPages["skin"] = (false, _skin);
            _lazyPages["layout"] = (false, _layout);
            _lazyPages["apps"] = (false, _apps);
        }
        _lazyPages["advanced"] = (false, _advanced);
        _lazyPages["backup"] = (false, _backup);
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;

        // 分组项（外观设置）只负责展开子菜单，不换页。
        if (value.Children is { Count: > 0 }) return;

        PageTitle = value.Header;
        if (value.Key == "overview")
        {
            CurrentPage = _overview;
            return;
        }

        if (!_lazyPages.TryGetValue(value.Key, out var entry)) return;
        if (!entry.Loaded)
        {
            _lazyPages[value.Key] = (true, entry.Page);
            LoadPage(entry.Page);
        }
        CurrentPage = entry.Page;
    }

    /// <summary>Frontend switch: fresh page VMs bound to the new distribution.</summary>
    private void RebuildPages()
    {
        BuildPages();
        InitializeLazyPages();

        // Stay on the same page; load the fresh instance immediately.
        if (SelectedNav is { Key: not "overview" } nav && _lazyPages.TryGetValue(nav.Key, out var entry))
        {
            _lazyPages[nav.Key] = (true, entry.Page);
            LoadPage(entry.Page);
            CurrentPage = entry.Page;
        }
        else
        {
            CurrentPage = _overview;
        }
    }

    private static void LoadPage(ObservableObject page)
    {
        switch (page)
        {
            case SchemaManagerViewModel schemas:
                schemas.LoadCommand.Execute(null);
                break;
            case SettingsEditorViewModel settings:
                settings.LoadCommand.Execute(null);
                break;
            // 三个外观子页共享编辑器：经壳转发一次加载即可。
            case AppearancePageViewModel appearance:
                appearance.Editor.LoadCommand.Execute(null);
                break;
            case AdvancedSettingsViewModel advanced:
                advanced.LoadCommand.Execute(null);
                break;
            case BackupRestoreViewModel backup:
                backup.LoadCommand.Execute(null);
                break;
        }
    }

    private static Geometry? Icon(string resourceKey)
        => Application.Current is { } app
           && app.TryGetResource(resourceKey, app.ActualThemeVariant, out var value)
           && value is Geometry geometry
            ? geometry
            : null;
}
