using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls.Notifications;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Core.Distributions;
using Erya.Services;

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

public partial class SchemaManagerViewModel : ObservableObject
{
    private readonly IRimeDistribution? _distribution;

    public SchemaManagerViewModel(IRimeDistribution? distribution) => _distribution = distribution;

    public ObservableCollection<SchemaItemViewModel> Schemas { get; } = [];

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

    /// <summary>Inputs of the 添加新方案 form.</summary>
    [ObservableProperty]
    private string _newSchemaId = "";

    [ObservableProperty]
    private string _newSchemaName = "";

    public bool HasHint => Hint.Length > 0;

    public bool HasError => ErrorMessage.Length > 0;

    public bool HasSchemas => Schemas.Count > 0;

    /// <summary>已启用方案间的顺序即 Ctrl+` 切换顺序。</summary>
    [RelayCommand]
    private void MoveUp(SchemaItemViewModel? item)
    {
        SwapWithNeighbor(item, direction: -1);
        OnPropertyChanged(nameof(HasSchemas));
    }

    [RelayCommand]
    private void MoveDown(SchemaItemViewModel? item) => SwapWithNeighbor(item, direction: 1);

    /// <summary>
    /// Swaps <paramref name="item"/> with the previous/next <b>selected</b>
    /// row, skipping unchecked rows in between — only the relative order of
    /// enabled schemas reaches the saved schema_list.
    /// </summary>
    private void SwapWithNeighbor(SchemaItemViewModel? item, int direction)
    {
        if (item is null || !item.IsSelected || IsLoading) return;

        int index = Schemas.IndexOf(item);
        if (index < 0) return;

        int neighbor = index + direction;
        while (neighbor >= 0 && neighbor < Schemas.Count && !Schemas[neighbor].IsSelected)
            neighbor += direction;
        if (neighbor < 0 || neighbor >= Schemas.Count) return;

        Schemas.Move(index, neighbor);
    }

    [RelayCommand]
    private async Task AddSchemaAsync()
    {
        if (_distribution is null || IsLoading || IsBusy) return;

        var id = NewSchemaId.Trim();
        if (id.Length == 0)
        {
            ErrorMessage = "请输入方案 ID（如 my_pinyin）。";
            return;
        }

        IsBusy = true;
        try
        {
            ErrorMessage = "";
            await _distribution.CreateSchemaStubAsync(id, NewSchemaName.Trim());
            NewSchemaId = "";
            NewSchemaName = "";
            await LoadAsync();

            // Offer the fresh schema pre-checked; saving is still explicit.
            var created = Schemas.FirstOrDefault(s => s.Id == id);
            if (created is not null) created.IsSelected = true;

            AppNotifications.Toast($"已创建 {id}.schema.yaml（回声脚手架），勾选保存后即可启用。", NotificationType.Success);
        }
        catch (Exception ex)
        {
            ErrorMessage = $"创建方案失败：{ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

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
}
