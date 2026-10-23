using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;
using Erya.Core.Packages;
using Erya.Localization;
using Erya.Services;
using Ursa.Controls;

namespace Erya.ViewModels;

public partial class SchemaItemViewModel : ObservableObject
{
    public SchemaItemViewModel(string id, string name, bool isSelected)
    {
        Id = id;
        Name = name;
        _isSelected = isSelected;
    }

    public string Id { get; }

    /// <summary>Display name; falls back to the id when the schema file carries no name.</summary>
    public string Name { get; }

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One entry of the built-in online package catalog, plus install state.</summary>
public partial class OnlinePackageViewModel(SchemaPackage package) : ObservableObject
{
    public SchemaPackage Package { get; } = package;

    public string Name => Package.Name;

    public string Description => Package.Description;

    public string Homepage => Package.Homepage;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isInstalling;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy))]
    private bool _isUninstalling;

    /// <summary>A marker schema of this package exists in the user directory.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InstallButtonText))]
    [NotifyPropertyChangedFor(nameof(InstallToolTip))]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private bool _isInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText))]
    private string? _status;

    public bool IsBusy => IsInstalling || IsUninstalling;

    /// <summary>已安装的包走同一安装流程：覆盖安装即升级。</summary>
    public string InstallButtonText => IsInstalled
        ? Loc.Get(LanguageManager.Instance.schemas_reinstallButton)
        : Loc.Get(LanguageManager.Instance.schemas_installButton);

    /// <summary>Icon-only 按钮的文字语义搬到 tooltip 里。</summary>
    public string InstallToolTip => IsInstalled
        ? Loc.Get(LanguageManager.Instance.schemas_reinstallTip)
        : Loc.Get(LanguageManager.Instance.schemas_installTip);

    /// <summary>Progress/result message when present, otherwise the idle 已安装 badge.</summary>
    public string? StatusText => Status is { Length: > 0 } status ? status : IsInstalled ? Loc.Get(LanguageManager.Instance.schemas_installedBadge) : null;
}

public partial class SchemaManagerViewModel : ObservableObject
{
    private readonly IRimeDistribution? _distribution;
    private readonly SchemaInstaller _installer;

    public SchemaManagerViewModel(IRimeDistribution? distribution, SchemaInstaller? installer = null)
    {
        _distribution = distribution;
        _installer = installer ?? new SchemaInstaller();
        foreach (var package in SchemaPackageCatalog.Load())
            OnlinePackages.Add(new OnlinePackageViewModel(package));
    }

    public ObservableCollection<SchemaItemViewModel> Schemas { get; } = [];

    /// <summary>Community packages installable straight from the catalog.</summary>
    public ObservableCollection<OnlinePackageViewModel> OnlinePackages { get; } = [];

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasHint))]
    private string _hint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    private string _errorMessage = "";

    public bool HasHint => Hint.Length > 0;

    public bool HasError => ErrorMessage.Length > 0;

    public bool HasSchemas => Schemas.Count > 0;

    // 顺序调整由 ReorderableItemsControl 拖拽完成（直接 Move 集合）；
    // 保存时按集合顺序取已勾选 id，未勾选行的位置不落盘。

    [RelayCommand]
    private async Task LoadAsync()
    {
        var m = LanguageManager.Instance;
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            if (_distribution is null)
            {
                Schemas.Clear();
                Hint = Loc.Format(m.schemas_noDistributionHint, InstallHints.ForCurrentOs);
                return;
            }

            var snapshot = await _distribution.GetSchemasAsync();
            Schemas.Clear();
            foreach (var schema in snapshot.Schemas)
                Schemas.Add(new SchemaItemViewModel(schema.Id, schema.Name, snapshot.SelectedSchemaIds.Contains(schema.Id)));

            RefreshInstalledStates();

            Hint = Schemas.Count == 0
                ? Loc.Get(m.schemas_emptyHint)
                : "";
            OnPropertyChanged(nameof(HasSchemas));
        }
        catch (Exception ex)
        {
            Hint = Loc.Format(m.schemas_loadFailed, ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    /// <summary>
    /// Probe each package's marker schemas against the user directory —
    /// runs on every page load so manual installs/deletes are picked up too.
    /// </summary>
    private void RefreshInstalledStates()
    {
        if (_distribution is null) return;
        foreach (var item in OnlinePackages)
            item.IsInstalled = item.Package.IsInstalledIn(_distribution.UserDir);
    }

    [RelayCommand]
    private async Task SaveAndDeployAsync()
    {
        var m = LanguageManager.Instance;
        if (_distribution is null || IsLoading || Schemas.Count == 0) return;

        IsBusy = true;
        try
        {
            ErrorMessage = "";
            var ids = Schemas.Where(s => s.IsSelected).Select(s => s.Id).ToList();
            await _distribution.SetSelectedSchemasAsync(ids);

            if (!_distribution.CanDeploy)
            {
                AppNotifications.Notify(Loc.Get(m.schemas_saveDoneTitle),
                    Loc.Get(m.schemas_saveNoDeployer),
                    NotificationType.Warning);
                return;
            }

            await _distribution.DeployAsync();
            AppNotifications.Toast(Loc.Get(m.schemas_saveDeployDoneToast), NotificationType.Success);
        }
        catch (Exception ex)
        {
            ErrorMessage = Loc.Format(m.schemas_saveFailed, ex.Message);
            // The error banner sits above the schema list; the save button is
            // in the page toolbar — also surface the failure as a toast.
            AppNotifications.Toast(Loc.Format(m.schemas_saveFailed, ex.Message), NotificationType.Error);
        }
        finally
        {
            IsBusy = false;
        }
    }

    /// <summary>One package operation at a time; other buttons stay clickable but the commands no-op.</summary>
    private bool AnyPackageBusy => OnlinePackages.Any(p => p.IsBusy);

    [RelayCommand]
    private async Task InstallAsync(OnlinePackageViewModel? item)
    {
        // No IsLoading guard here: the online section is usable before the
        // first schema-list load has run (construction leaves IsLoading=true).
        if (item is null || _distribution is null || AnyPackageBusy) return;

        var m = LanguageManager.Instance;
        item.IsInstalling = true;
        item.Status = Loc.Get(m.schemas_prepareDownload);
        var progress = new Progress<SchemaInstallProgress>(p =>
            item.Status = p.Percent is { } percent ? $"{p.Phase} {percent:0}%" : p.Phase);
        try
        {
            ErrorMessage = "";
            var report = await _installer.InstallAsync(item.Package, _distribution.UserDir, progress);
            await LoadAsync(); // the fresh schemas show up in the list
            var enableNote = await EnableMarkerSchemasAsync(item.Package);

            var deployNote = _distribution.CanDeploy
                ? await DeployQuietlyAsync()
                : InstallHints.DeployerMissingHint(_distribution);
            item.Status = Loc.Format(m.schemas_installedFilesStatus, report.InstalledFiles);
            AppNotifications.Notify(Loc.Get(m.schemas_installDoneTitle),
                Loc.Format(m.schemas_installDoneBody, item.Name, report.InstalledFiles) +
                (report.BackupPath is { } backup ? Loc.Format(m.schemas_installBackupNote, backup) : "") +
                enableNote +
                deployNote,
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            item.Status = null;
            ErrorMessage = Loc.Format(m.schemas_installFailed, item.Name, ex.Message);
        }
        finally
        {
            item.IsInstalling = false;
        }
    }

    /// <summary>
    /// A freshly installed package is invisible to RIME until one of its
    /// schemas is in schema_list — tick the package's marker schema when none
    /// of them is enabled yet, so install plus the deploy that follows yields
    /// a schema the user can actually switch to. A reinstall keeps whatever
    /// the user has arranged.
    /// </summary>
    private async Task<string> EnableMarkerSchemasAsync(SchemaPackage package)
    {
        var m = LanguageManager.Instance;
        var markerIds = package.MarkerSchemas.Select(SchemaIdOf).ToHashSet(StringComparer.Ordinal);
        var rows = Schemas.Where(s => markerIds.Contains(s.Id)).ToList();
        if (rows.Count == 0) return Loc.Get(m.schemas_enableHintManual);
        if (rows.Any(r => r.IsSelected)) return "";

        foreach (var row in rows) row.IsSelected = true;
        try
        {
            await _distribution!.SetSelectedSchemasAsync(Schemas.Where(s => s.IsSelected).Select(s => s.Id).ToList());
            return Loc.Format(m.schemas_autoEnabledNote, string.Join("、", rows.Select(r => r.Name)));
        }
        catch (Exception ex)
        {
            foreach (var row in rows) row.IsSelected = false;
            return Loc.Format(m.schemas_autoEnableFailed, ex.Message);
        }
    }

    /// <summary>Schema id of a marker file, derived the way the catalog does (rime_ice.schema.yaml → rime_ice).</summary>
    private static string SchemaIdOf(string markerFile)
        => Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(markerFile));

    /// <summary>Deploy after install; a deploy failure must not read as an install failure.</summary>
    private async Task<string> DeployQuietlyAsync()
    {
        try
        {
            await _distribution!.DeployAsync();
            return Loc.Get(LanguageManager.Instance.schemas_redeployDone);
        }
        catch (Exception ex)
        {
            return Loc.Format(LanguageManager.Instance.schemas_filesReadyDeployFailed, ex.Message);
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(OnlinePackageViewModel? item)
    {
        if (item is null || _distribution is null || AnyPackageBusy) return;

        var m = LanguageManager.Instance;

        // Destructive: delete only the package's own files (manifest fetched
        // from the repo), full backup first — confirm before touching disk.
        var result = await OverlayDialog.ShowStandardAsync(
            BuildUninstallConfirmContent(item.Name, _distribution.UserDir), null, "main",
            new OverlayDialogOptions
            {
                Title = Loc.Get(m.schemas_uninstallConfirmTitle),
                Mode = DialogMode.Question,
                Buttons = DialogButton.OKCancel,
            });
        if (result != DialogResult.OK) return;

        item.IsUninstalling = true;
        item.Status = Loc.Get(m.schemas_prepareManifest);
        var progress = new Progress<SchemaInstallProgress>(p => item.Status = p.Phase);
        try
        {
            ErrorMessage = "";
            var report = await _installer.UninstallAsync(item.Package, _distribution.UserDir, progress);
            await LoadAsync();

            var deployNote = _distribution.CanDeploy
                ? await DeployQuietlyAsync()
                : InstallHints.DeployerMissingHint(_distribution);
            item.Status = Loc.Format(m.schemas_deletedFilesStatus, report.DeletedFiles);
            AppNotifications.Notify(Loc.Get(m.schemas_uninstallDoneTitle),
                Loc.Format(m.schemas_uninstallDoneBody, item.Name, report.DeletedFiles)
                + (report.BackupPath is { } backup ? Loc.Format(m.schemas_uninstallBackupNote, backup) : "")
                + deployNote
                + Loc.Get(m.schemas_uninstallTailNote),
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            item.Status = null;
            ErrorMessage = Loc.Format(m.schemas_uninstallFailed, item.Name, ex.Message);
        }
        finally
        {
            item.IsUninstalling = false;
        }
    }

    private static Control BuildUninstallConfirmContent(string name, string userDir)
    {
        var m = LanguageManager.Instance;
        return new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = Loc.Format(m.schemas_uninstallConfirmBody, name),
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = Loc.Format(m.schemas_uninstallConfirmUserDir, userDir), TextWrapping = TextWrapping.Wrap, Opacity = 0.72 },
                new TextBlock
                {
                    Text = Loc.Get(m.schemas_uninstallConfirmBackup),
                    TextWrapping = TextWrapping.Wrap,
                },
            }
        };
    }
}
