using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using Erya.Core.Distributions;
using Erya.Services;

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
    private readonly BackupRestoreViewModel _backup;
    private bool _schemasLoaded;
    private bool _backupLoaded;

    public IReadOnlyList<NavItem> NavItems { get; }

    [ObservableProperty]
    private NavItem? _selectedNav;

    [ObservableProperty]
    private ObservableObject? _currentPage;

    [ObservableProperty]
    private string _pageTitle = "";

    public ShellViewModel(DistributionManager distributions)
    {
        _overview = new MainViewModel(distributions);
        _schemas = new SchemaManagerViewModel(_overview.Distribution);
        _backup = new BackupRestoreViewModel(_overview.Distribution);

        NavItems =
        [
            new NavItem("overview", "概览", Icon("SemiIconHome")),
            new NavItem("schemas", "方案管理", Icon("SemiIconCheckList")),
            new NavItem("backup", "备份恢复", Icon("SemiIconRestore")),
        ];

        // Two-way binding pushes this into NavMenu, selecting the item visually.
        _selectedNav = NavItems[0];
        CurrentPage = _overview;
        PageTitle = _selectedNav.Header;
    }

    partial void OnSelectedNavChanged(NavItem? value)
    {
        if (value is null) return;

        PageTitle = value.Header;
        switch (value.Key)
        {
            case "overview":
                CurrentPage = _overview;
                break;
            case "schemas":
                if (!_schemasLoaded)
                {
                    _schemasLoaded = true;
                    _schemas.LoadCommand.Execute(null);
                }
                CurrentPage = _schemas;
                break;
            case "backup":
                if (!_backupLoaded)
                {
                    _backupLoaded = true;
                    _backup.LoadCommand.Execute(null);
                }
                CurrentPage = _backup;
                break;
            default:
                // Placeholder for pages not yet implemented — acknowledge instead of switching.
                AppNotifications.Toast("「" + value.Header + "」即将上线。", NotificationType.Information);
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
