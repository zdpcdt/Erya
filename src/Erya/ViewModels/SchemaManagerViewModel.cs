using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;
using Erya.Core.Packages;
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
    public string InstallButtonText => IsInstalled ? "重新安装" : "安装";

    /// <summary>Icon-only 按钮的文字语义搬到 tooltip 里。</summary>
    public string InstallToolTip => IsInstalled
        ? "重新安装：重新下载并覆盖写入用户目录（即升级），完成后自动重新部署"
        : "安装：下载并写入 RIME 用户目录，完成后自动重新部署";

    /// <summary>Progress/result message when present, otherwise the idle 已安装 badge.</summary>
    public string? StatusText => Status is { Length: > 0 } status ? status : IsInstalled ? "已安装" : null;
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
        IsLoading = true;
        ErrorMessage = "";
        try
        {
            if (_distribution is null)
            {
                Schemas.Clear();
                Hint = "未检测到 RIME 发行版：" + InstallHints.ForCurrentOs;
                return;
            }

            var snapshot = await _distribution.GetSchemasAsync();
            Schemas.Clear();
            foreach (var schema in snapshot.Schemas)
                Schemas.Add(new SchemaItemViewModel(schema.Id, schema.Name, snapshot.SelectedSchemaIds.Contains(schema.Id)));

            RefreshInstalledStates();

            Hint = Schemas.Count == 0
                ? "未发现任何输入方案：请先在小狼毫中部署一次（方案文件会出现在用户目录），或先安装方案（如雾凇拼音）。"
                : "";
            OnPropertyChanged(nameof(HasSchemas));
        }
        catch (Exception ex)
        {
            Hint = $"读取方案列表失败：{ex.Message}";
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
        if (_distribution is null || IsLoading || Schemas.Count == 0) return;

        IsBusy = true;
        try
        {
            ErrorMessage = "";
            var ids = Schemas.Where(s => s.IsSelected).Select(s => s.Id).ToList();
            await _distribution.SetSelectedSchemasAsync(ids);

            if (!_distribution.CanDeploy)
            {
                AppNotifications.Notify("方案已保存",
                    "已写入 default.custom.yaml，但未找到部署器，需手动重新部署后生效。",
                    NotificationType.Warning);
                return;
            }

            await _distribution.DeployAsync();
            AppNotifications.Toast("方案已保存并触发重新部署。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"保存失败：{ex.Message}";
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

        item.IsInstalling = true;
        item.Status = "准备下载…";
        var progress = new Progress<SchemaInstallProgress>(p =>
            item.Status = p.Percent is { } percent ? $"{p.Phase} {percent:0}%" : p.Phase);
        try
        {
            ErrorMessage = "";
            var report = await _installer.InstallAsync(item.Package, _distribution.UserDir, progress);
            await LoadAsync(); // the fresh schemas show up in the list, waiting to be enabled

            var deployNote = _distribution.CanDeploy
                ? await DeployQuietlyAsync()
                : InstallHints.DeployerMissingHint(_distribution);
            item.Status = $"已安装 {report.InstalledFiles} 个文件";
            AppNotifications.Notify("方案安装完成",
                $"已将 {item.Name} 的 {report.InstalledFiles} 个文件写入用户目录。" +
                (report.BackupPath is { } backup ? $"安装前的原配置已备份：{backup}。" : "") +
                deployNote +
                "在上方列表勾选启用新方案后保存即可使用。",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            item.Status = null;
            ErrorMessage = $"安装 {item.Name} 失败：{ex.Message}";
        }
        finally
        {
            item.IsInstalling = false;
        }
    }

    /// <summary>Deploy after install; a deploy failure must not read as an install failure.</summary>
    private async Task<string> DeployQuietlyAsync()
    {
        try
        {
            await _distribution!.DeployAsync();
            return "已触发重新部署。";
        }
        catch (Exception ex)
        {
            return $"文件已就位，但重新部署失败：{ex.Message}";
        }
    }

    [RelayCommand]
    private async Task UninstallAsync(OnlinePackageViewModel? item)
    {
        if (item is null || _distribution is null || AnyPackageBusy) return;

        // Destructive: delete only the package's own files (manifest fetched
        // from the repo), full backup first — confirm before touching disk.
        var result = await OverlayDialog.ShowStandardAsync(
            BuildUninstallConfirmContent(item.Name, _distribution.UserDir), null, "main",
            new OverlayDialogOptions
            {
                Title = "确认删除方案包",
                Mode = DialogMode.Question,
                Buttons = DialogButton.OKCancel,
            });
        if (result != DialogResult.OK) return;

        item.IsUninstalling = true;
        item.Status = "准备获取文件清单…";
        var progress = new Progress<SchemaInstallProgress>(p => item.Status = p.Phase);
        try
        {
            ErrorMessage = "";
            var report = await _installer.UninstallAsync(item.Package, _distribution.UserDir, progress);
            await LoadAsync();

            var deployNote = _distribution.CanDeploy
                ? await DeployQuietlyAsync()
                : InstallHints.DeployerMissingHint(_distribution);
            item.Status = $"已删除 {report.DeletedFiles} 个文件";
            AppNotifications.Notify("方案包已删除",
                $"已从用户目录删除 {item.Name} 的 {report.DeletedFiles} 个文件。"
                + (report.BackupPath is { } backup ? $"删除前的完整备份：{backup}。" : "")
                + deployNote
                + "若启用列表仍勾选已删方案，重新保存一次即可清除。",
                NotificationType.Success);
        }
        catch (Exception ex)
        {
            item.Status = null;
            ErrorMessage = $"删除 {item.Name} 失败：{ex.Message}";
        }
        finally
        {
            item.IsUninstalling = false;
        }
    }

    private static Control BuildUninstallConfirmContent(string name, string userDir)
        => new StackPanel
        {
            Spacing = 8,
            Children =
            {
                new TextBlock
                {
                    Text = $"将删除 {name} 安装到用户目录的全部文件；你手动添加的其他文件不受影响。",
                    TextWrapping = TextWrapping.Wrap,
                },
                new TextBlock { Text = $"用户目录：{userDir}", TextWrapping = TextWrapping.Wrap, Opacity = 0.72 },
                new TextBlock
                {
                    Text = "删除前会自动生成完整备份（pre-uninstall-*.zip），如有问题可通过备份恢复回滚。",
                    TextWrapping = TextWrapping.Wrap,
                },
            }
        };
}
