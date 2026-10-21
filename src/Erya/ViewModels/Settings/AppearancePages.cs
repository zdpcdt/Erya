using System.Collections.Generic;
using System.ComponentModel;
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

/// <summary>皮肤库页：配色选择 + 亮/暗双预览 + 皮肤列表（左）与选中皮肤编辑面板（右）。</summary>
public sealed partial class SkinPageViewModel : AppearancePageViewModel
{
    public SkinPageViewModel(SettingsEditorViewModel editor) : base(editor, "skin")
    {
        editor.PropertyChanged += OnEditorChangedForSelection;
        if (editor.HasSchemeLibrary) SelectDefault(); // 编辑器已先行加载（如测试）的场景
    }

    [ObservableProperty]
    private ColorSchemeViewModel? _selectedScheme;

    /// <summary>
    /// 重新加载或库变更（增删/编辑）后校验选中项：失效（被删/列表刷新）时回落到
    /// 第一套——ListBox 在选中项被删时会写回 null，右栏不能因此空掉。
    /// </summary>
    private void OnEditorChangedForSelection(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(SettingsEditorViewModel.Sections)
            or nameof(SettingsEditorViewModel.HasSchemeLibrary)
            or nameof(SettingsEditorViewModel.IsDirty))) return;
        SelectDefault();
    }

    private void SelectDefault()
    {
        var schemes = Editor.SchemeLibrary?.Schemes;
        if (schemes is null || schemes.Count == 0)
        {
            SelectedScheme = null;
            return;
        }
        if (SelectedScheme is null || !schemes.Contains(SelectedScheme))
            SelectedScheme = schemes[0];
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
