using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Erya.Views;
using Ursa.Controls;

namespace Erya.ViewModels.Settings;

/// <summary>
/// 外观目标的三个子页共用的壳：皮肤库 / 候选窗排版 / 应用行为共享同一个
/// <see cref="SettingsEditorViewModel"/>（同一份字段、皮肤库、预览与保存动作），
/// 各自只显示 <see cref="Sections"/>（按 schema 的 group 过滤）。
/// 编辑器的属性变化原样转发，子页绑定无需感知共享。
/// </summary>
public abstract partial class AppearancePageViewModel : ObservableObject
{
    protected AppearancePageViewModel(SettingsEditorViewModel editor, string group)
    {
        Editor = editor;
        Group = group;
        editor.PropertyChanged += OnEditorChanged;
    }

    public SettingsEditorViewModel Editor { get; }

    public string Group { get; }

    public IReadOnlyList<SettingsSectionViewModel> Sections => Editor.SectionsFor(Group);

    public bool IsSupported => Editor.IsSupported;

    public string UnsupportedReason => Editor.UnsupportedReason;

    public bool IsLoading => Editor.IsLoading;

    public bool IsBusy => Editor.IsBusy;

    public string ErrorMessage => Editor.ErrorMessage;

    public bool HasError => Editor.HasError;

    public string Subtitle => Editor.Subtitle;

    public bool IsDirty => Editor.IsDirty;

    public ColorSchemeLibraryViewModel? SchemeLibrary => Editor.SchemeLibrary;

    public bool HasSchemeLibrary => Editor.HasSchemeLibrary;

    public CandidatePreviewViewModel? CandidatePreview => Editor.CandidatePreview;

    public CandidatePreviewViewModel? CandidatePreviewDark => Editor.CandidatePreviewDark;

    public bool HasCandidatePreview => Editor.HasCandidatePreview;

    /// <summary>
    /// 是否鼠须管：只有它支持皮肤级字体/排版（SquirrelTheme.swift 里皮肤键覆盖全局
    /// style 键）；小狼毫从皮肤只读颜色，皮肤级排版控件对它是 placebo。
    /// </summary>
    public bool IsSquirrel => Editor.UiConfigFile == "squirrel.yaml";

    public IAsyncRelayCommand LoadCommand => Editor.LoadCommand;

    public IAsyncRelayCommand SaveAndDeployCommand => Editor.SaveAndDeployCommand;

    private void OnEditorChanged(object? sender, PropertyChangedEventArgs e)
    {
        // 与编辑器同名属性直接转发（含 Sections——重新加载后列表是新实例）。
        OnPropertyChanged(e.PropertyName);
    }
}

/// <summary>
/// 皮肤库页（Windows 对比度主题式）：顶部皮肤画廊（点击应用为亮色主题）+
/// 亮色/暗色主题两个 Cell；「编辑」打开 Ursa 标准对话框（内置皮肤可调整，
/// 确定时另存为新的自定义皮肤）。
/// </summary>
public sealed partial class SkinPageViewModel : AppearancePageViewModel
{
    private ChoiceFieldViewModel? _watchedLight;
    private ChoiceFieldViewModel? _watchedDark;

    public SkinPageViewModel(SettingsEditorViewModel editor) : base(editor, "skin")
    {
        editor.PropertyChanged += OnEditorChangedForScheme;
        if (editor.SchemeLibrary is { } library)
        {
            library.EditsPending += OnLibraryEdited;
            HookFields();
        }
    }

    /// <summary>style/color_scheme 字段（画廊点击与 Cell 下拉都写它）。</summary>
    public ChoiceFieldViewModel? LightSchemeField => FieldOf("style/color_scheme");

    public ChoiceFieldViewModel? DarkSchemeField => FieldOf("style/color_scheme_dark");

    private ChoiceFieldViewModel? FieldOf(string keyPath)
        => Sections.FirstOrDefault(s => s.Group == "skin")?.Fields
            .OfType<ChoiceFieldViewModel>().FirstOrDefault(f => f.KeyPath == keyPath);

    [ObservableProperty]
    private string _searchText = "";

    partial void OnSearchTextChanged(string value) => OnPropertyChanged(nameof(FilteredSchemes));

    /// <summary>画廊列表：按名/类型过滤（大小写不敏感），自定义置顶、内置按名排序。</summary>
    public IEnumerable<ColorSchemeViewModel> FilteredSchemes
    {
        get
        {
            var query = SearchText.Trim();
            return (Editor.SchemeLibrary?.Schemes ?? Enumerable.Empty<ColorSchemeViewModel>())
                .Where(s => query.Length == 0
                            || s.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || s.KindText.Contains(query, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(s => !s.IsShipped)
                .ThenBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
    }

    /// <summary>卡片主体点击 / 角上 ☀：应用为亮色主题（待保存编辑，随「保存并部署」落盘）。</summary>
    [RelayCommand]
    private void UseAsLightScheme(ColorSchemeViewModel? scheme)
    {
        if (scheme?.HasValidName == true && LightSchemeField is { } field)
            field.Selected = scheme.Name.Trim();
    }

    /// <summary>角上 ☾：应用为暗色主题。</summary>
    [RelayCommand]
    private void UseAsDarkScheme(ColorSchemeViewModel? scheme)
    {
        if (scheme?.HasValidName == true && DarkSchemeField is { } field)
            field.Selected = scheme.Name.Trim();
    }

    [RelayCommand]
    private Task EditSchemeAsync(ColorSchemeViewModel? scheme) => EditAsync(scheme);

    [RelayCommand]
    private Task EditLightSchemeAsync() => EditAsync(Find(LightSchemeField?.Selected));

    [RelayCommand]
    private Task EditDarkSchemeAsync() => EditAsync(Find(DarkSchemeField?.Selected));

    private ColorSchemeViewModel? Find(string? name)
        => Editor.SchemeLibrary?.Schemes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 编辑对话框：确定 = 保留（内置皮肤 = 另存为新的自定义皮肤），取消 = 还原。
    /// 对话框里的删除按钮可能在等待期间把皮肤从库中移走——返回后按存在性分流。
    /// </summary>
    private async Task EditAsync(ColorSchemeViewModel? scheme)
    {
        if (scheme is null || SchemeLibrary is null) return;
        var fields = Sections.SelectMany(s => s.Fields).ToList();
        var vm = new SchemeEditDialogViewModel(scheme, fields, SchemeLibrary, IsSquirrel);
        var result = await OverlayDialog.ShowStandardAsync(
            new SchemeEditDialog(), vm, "main",
            new OverlayDialogOptions
            {
                Title = vm.Title,
                Mode = DialogMode.None,
                Buttons = DialogButton.OKCancel,
            });
        vm.Detach(); // 解开预览对字段的订阅
        if (!SchemeLibrary.Schemes.Contains(scheme)) return; // 已在对话框里删除
        if (result == DialogResult.OK) vm.Commit();
        else vm.Revert();
    }

    /// <summary>重新加载：字段/皮肤库都是新实例——重新挂钩并刷新角标与过滤列表。</summary>
    private void OnEditorChangedForScheme(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsEditorViewModel.Sections))
        {
            OnPropertyChanged(nameof(LightSchemeField));
            OnPropertyChanged(nameof(DarkSchemeField));
            HookFields();
        }
        if (e.PropertyName == nameof(SettingsEditorViewModel.HasSchemeLibrary)
            && Editor.SchemeLibrary is { } library)
        {
            library.EditsPending += OnLibraryEdited;
        }
        RefreshCurrentFlags();
        OnPropertyChanged(nameof(FilteredSchemes));
    }

    private void OnLibraryEdited(object? sender, EventArgs e)
    {
        RefreshCurrentFlags();
        OnPropertyChanged(nameof(FilteredSchemes));
    }

    /// <summary>亮/暗槽位字段变化 → 重算每张卡的当前角色角标。</summary>
    private void HookFields()
    {
        if (_watchedLight is { } oldLight) oldLight.PropertyChanged -= OnSlotChanged;
        if (_watchedDark is { } oldDark) oldDark.PropertyChanged -= OnSlotChanged;
        _watchedLight = LightSchemeField;
        _watchedDark = DarkSchemeField;
        if (_watchedLight is { } light) light.PropertyChanged += OnSlotChanged;
        if (_watchedDark is { } dark) dark.PropertyChanged += OnSlotChanged;
        RefreshCurrentFlags();
    }

    private void OnSlotChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ChoiceFieldViewModel.Selected)) RefreshCurrentFlags();
    }

    private void RefreshCurrentFlags()
    {
        var light = LightSchemeField?.Selected;
        var dark = DarkSchemeField?.Selected;
        foreach (var scheme in Editor.SchemeLibrary?.Schemes ?? Enumerable.Empty<ColorSchemeViewModel>())
        {
            scheme.IsCurrentLight = string.Equals(scheme.Name, light, StringComparison.OrdinalIgnoreCase);
            scheme.IsCurrentDark = string.Equals(scheme.Name, dark, StringComparison.OrdinalIgnoreCase);
        }
    }
}

/// <summary>候选窗排版页：顶部固定亮/暗双预览 + 按类型分组的排版设置。</summary>
public sealed class LayoutPageViewModel : AppearancePageViewModel
{
    public LayoutPageViewModel(SettingsEditorViewModel editor) : base(editor, "layout")
    {
    }
}

/// <summary>应用行为页：按应用设置输入行为（app_options）。</summary>
public sealed class AppsPageViewModel : AppearancePageViewModel
{
    public AppsPageViewModel(SettingsEditorViewModel editor) : base(editor, "apps")
    {
    }
}
