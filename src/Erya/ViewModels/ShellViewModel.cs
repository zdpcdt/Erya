using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;
using Erya.Core.Localization;
using Erya.Localization;
using Erya.Services;
using Erya.ViewModels.Advanced;
using Erya.ViewModels.Settings;
using Irihi.Lingua;

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
    // 语言偏好只随显式传入的实例持久化；测试传 null 即不落盘。
    private readonly AppPreferences? _preferences;
    // 已应用语言（ CultureName）；构造期应用持久化语言时跳过重复切换。
    private string _appliedLanguage = "zh-Hans";
    private SchemaManagerViewModel _schemas = null!;
    private SettingsEditorViewModel _inputSettings = null!;
    private SettingsEditorViewModel _appearance = null!;
    private SkinPageViewModel _skin = null!;
    private LayoutPageViewModel _layout = null!;
    private AppsPageViewModel _apps = null!;
    private AdvancedSettingsViewModel _advanced = null!;
    private BackupRestoreViewModel _backup = null!;
    private AboutViewModel _about = null!;
    private Dictionary<string, (bool Loaded, ObservableObject Page)> _lazyPages = [];
    private IRimeDistribution? _distribution;

    /// <summary>CulturePicker 驱动的 Lingua 管理器（App 层字符串）。</summary>
    public IList<ILinguaManager> LinguaManagers { get; } = [LanguageManager.Instance];

    /// <summary>语言选择器条目；显示名固定用各自语言书写。</summary>
    public IReadOnlyList<LinguaCulture> Cultures { get; } =
    [
        new() { CultureName = "zh-Hans", DisplayName = "简体中文", ShortDisplayName = "简" },
        new() { CultureName = "zh-Hant", DisplayName = "繁體中文", ShortDisplayName = "繁" },
        new() { CultureName = "en", DisplayName = "English", ShortDisplayName = "EN" },
    ];

    [ObservableProperty]
    private LinguaCulture? _selectedCulture;

    [ObservableProperty]
    private IReadOnlyList<NavItem> _navItems = [];

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
        _preferences = preferences;
        _overview = new MainViewModel(distributions, preferences);
        BuildPages();
        // A frontend switch rebuilds every page against the new distribution;
        // unsaved edits on other pages are deliberately dropped (rare action).
        _overview.DistributionChanged += RebuildPages;

        // 持久化语言在页面构建前应用，首帧即正确语言（App 组合根负责传入偏好）。
        if (preferences?.Language is { } language)
        {
            _appliedLanguage = language;
            LanguageManager.Instance.UpdateCulture(new CultureInfo(language));
            CoreLoc.Language = language;
        }
        BuildNav();
        InitializeLazyPages();

        // Two-way binding pushes this into NavMenu, selecting the item visually.
        _selectedNav = NavItems[0];
        CurrentPage = _overview;
        PageTitle = _selectedNav.Header;
        SelectedCulture = Cultures.FirstOrDefault(c => c.CultureName == _appliedLanguage) ?? Cultures[0];
    }

    private void BuildNav()
    {
        var m = LanguageManager.Instance;
        List<NavItem> items =
        [
            new NavItem("overview", Loc.Get(m.nav_overview), Icon("SemiIconHome")),
            new NavItem("schemas", Loc.Get(m.nav_schemas), Icon("SemiIconCheckList")),
            new NavItem("inputSettings", Loc.Get(m.nav_inputSettings), Icon("SemiIconSetting")),
        ];
        if (_showAppearance)
        {
            items.Add(new NavItem("appearance", Loc.Get(m.nav_appearance), Icon("SemiIconColorPalette"),
            [
                new NavItem("skin", Loc.Get(m.nav_skin), Icon("SemiIconColorPalette")),
                new NavItem("layout", Loc.Get(m.nav_layout), Icon("SemiIconSetting")),
                new NavItem("apps", Loc.Get(m.nav_apps), Icon("SemiIconDesktop")),
            ]));
        }
        items.Add(new NavItem("advanced", Loc.Get(m.nav_advanced), Icon("SemiIconCode")));
        items.Add(new NavItem("backup", Loc.Get(m.nav_backup), Icon("SemiIconRestore")));
        items.Add(new NavItem("about", Loc.Get(m.nav_about), Icon("SemiIconInfoCircle")));
        NavItems = items;
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
        _about = new AboutViewModel();
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
        _lazyPages["about"] = (false, _about);
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

    partial void OnSelectedCultureChanged(LinguaCulture? value)
    {
        // 构造期设置初始选择时 _appliedLanguage 已同步，这里自然跳过。
        if (value is not null)
            ApplyLanguage(value.CultureName);
    }

    /// <summary>
    /// 换语言：Lingua 全量推送（picker 可能已驱动过，重复 UpdateCulture 幂等）、
    /// Core 层字典切换、偏好落盘，然后重建导航与全部页面——页面 VM 在构造时
    /// 同步读取当前语言，设置页的 schema 也按新语言重载。
    /// </summary>
    private void ApplyLanguage(string cultureName)
    {
        if (cultureName == _appliedLanguage) return;
        _appliedLanguage = cultureName;

        LanguageManager.Instance.UpdateCulture(new CultureInfo(cultureName));
        CoreLoc.Language = cultureName;
        if (_preferences is not null)
        {
            _preferences.Language = cultureName;
            _preferences.Save();
        }

        RebuildNav();
        RebuildPages();
        _overview.RefreshLocalizedTexts();
    }

    /// <summary>换语言后导航标题重建；选中项按 Key 映射到新实例，保持当前页。</summary>
    private void RebuildNav()
    {
        var previousKey = SelectedNav?.Key;
        BuildNav();
        // 直写字段绕开 OnSelectedNavChanged（避免一次多余的页面加载），再通知绑定。
        _selectedNav = previousKey is { } key
            ? NavItems.SelectMany(item => item.Children is { Count: > 0 } children ? children : [item])
                .FirstOrDefault(item => item.Key == key)
            : null;
        PageTitle = SelectedNav?.Header ?? "";
        OnPropertyChanged(nameof(SelectedNav));
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
