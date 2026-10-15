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

/// <summary>Sidebar entry; selection is driven by NavMenu's TwoWay SelectedItem binding.</summary>
public sealed record NavItem(string Key, string Header, Geometry? Icon)
{
    // Fallback display (container renders ToString) should never leak internals.
    public override string ToString() => Header;
}

public partial class ShellViewModel : ObservableObject
{
    private readonly MainViewModel _overview;
    private readonly SchemaManagerViewModel _schemas;
    private readonly SettingsEditorViewModel _inputSettings;
    private readonly SettingsEditorViewModel _appearance;
    private readonly AdvancedSettingsViewModel _advanced;
    private readonly BackupRestoreViewModel _backup;
    private readonly Dictionary<string, (bool Loaded, ObservableObject Page)> _lazyPages = [];
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

    public ShellViewModel(DistributionManager distributions)
    {
        _overview = new MainViewModel(distributions);
        _distribution = _overview.Distribution;
        _schemas = new SchemaManagerViewModel(_distribution);
        _inputSettings = new SettingsEditorViewModel(_distribution, "default");
        _appearance = new SettingsEditorViewModel(_distribution, "ui");
        _advanced = new AdvancedSettingsViewModel(_distribution);
        _backup = new BackupRestoreViewModel(_distribution);

        NavItems =
        [
            new NavItem("overview", "概览", Icon("SemiIconHome")),
            new NavItem("schemas", "方案管理", Icon("SemiIconCheckList")),
            new NavItem("inputSettings", "输入设置", Icon("SemiIconSetting")),
            new NavItem("appearance", "外观设置", Icon("SemiIconColorPalette")),
            new NavItem("advanced", "高级设置", Icon("SemiIconCode")),
            new NavItem("backup", "备份恢复", Icon("SemiIconRestore")),
        ];
        _lazyPages["schemas"] = (false, _schemas);
        _lazyPages["inputSettings"] = (false, _inputSettings);
        _lazyPages["appearance"] = (false, _appearance);
        _lazyPages["advanced"] = (false, _advanced);
        _lazyPages["backup"] = (false, _backup);

        // Two-way binding pushes this into NavMenu, selecting the item visually.
        _selectedNav = NavItems[0];
        CurrentPage = _overview;
        PageTitle = _selectedNav.Header;
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;

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
            switch (entry.Page)
            {
                case SchemaManagerViewModel schemas:
                    schemas.LoadCommand.Execute(null);
                    break;
                case SettingsEditorViewModel settings:
                    settings.LoadCommand.Execute(null);
                    break;
                case AdvancedSettingsViewModel advanced:
                    advanced.LoadCommand.Execute(null);
                    break;
                case BackupRestoreViewModel backup:
                    backup.LoadCommand.Execute(null);
                    break;
            }
        }
        CurrentPage = entry.Page;
    }

    private static Geometry? Icon(string resourceKey)
        => Application.Current is { } app
           && app.TryGetResource(resourceKey, app.ActualThemeVariant, out var value)
           && value is Geometry geometry
            ? geometry
            : null;
}
