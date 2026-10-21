using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

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
/// 亮色/暗色主题两个 Cell；「编辑」进入页面内深一层（<see cref="EditingScheme"/>，
/// 面包屑返回），不弹 Dialog 也不占 NavMenu 层级。
/// </summary>
public sealed partial class SkinPageViewModel : AppearancePageViewModel
{
    public SkinPageViewModel(SettingsEditorViewModel editor) : base(editor, "skin")
    {
        editor.PropertyChanged += OnEditorChangedForScheme;
    }

    /// <summary>正在深页编辑的皮肤；null = 画廊视图。</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EditingSchemeIsNull))]
    private ColorSchemeViewModel? _editingScheme;

    /// <summary>视图切换用：true = 画廊，false = 深页编辑。</summary>
    public bool EditingSchemeIsNull => EditingScheme is null;

    /// <summary>style/color_scheme 字段（schema skinChoice 节加载，画廊点击与 Cell 下拉都写它）。</summary>
    public ChoiceFieldViewModel? LightSchemeField => FieldOf("style/color_scheme");

    public ChoiceFieldViewModel? DarkSchemeField => FieldOf("style/color_scheme_dark");

    private ChoiceFieldViewModel? FieldOf(string keyPath)
        => Sections.FirstOrDefault(s => s.Group == "skin")?.Fields
            .OfType<ChoiceFieldViewModel>().FirstOrDefault(f => f.KeyPath == keyPath);

    /// <summary>画廊卡片点击：应用为亮色主题（待保存编辑，随本页「保存并部署」落盘）。</summary>
    [RelayCommand]
    private void UseAsLightScheme(ColorSchemeViewModel? scheme)
    {
        if (scheme?.HasValidName == true && LightSchemeField is { } field)
            field.Selected = scheme.Name.Trim();
    }

    [RelayCommand]
    private void EditLightScheme() => EditingScheme = Find(LightSchemeField?.Selected);

    [RelayCommand]
    private void EditDarkScheme() => EditingScheme = Find(DarkSchemeField?.Selected);

    [RelayCommand]
    private void BackToGallery() => EditingScheme = null;

    private ColorSchemeViewModel? Find(string? name)
        => Editor.SchemeLibrary?.Schemes.FirstOrDefault(s => string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 重新加载后刷新字段引用；正在编辑的皮肤被删/被重载替换时退回画廊，
    /// 深页永远绑定一个真实存在的皮肤。
    /// </summary>
    private void OnEditorChangedForScheme(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(SettingsEditorViewModel.Sections)
            && e.PropertyName != nameof(SettingsEditorViewModel.IsDirty)) return;
        OnPropertyChanged(nameof(LightSchemeField));
        OnPropertyChanged(nameof(DarkSchemeField));
        var schemes = Editor.SchemeLibrary?.Schemes;
        if (EditingScheme is { } editing && (schemes is null || !schemes.Contains(editing)))
            EditingScheme = null;
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
