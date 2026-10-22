using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Erya.ViewModels.Settings;

/// <summary>
/// 皮肤编辑对话框（窗口级），两种模式：
/// <para><b>草稿</b>（新建/内置复制）——编辑一个未入库的副本，从第一下起改的就是
/// 自己的新皮肤：确定 = 入库为自定义皮肤，取消 = 丢弃（无还原动作）。</para>
/// <para><b>已有</b>（自定义皮肤）——编辑直接落在库中实例上：确定 = 保留，取消 =
/// 还原开框快照。</para>
/// 字体与排版组只在鼠须管下显示（它支持皮肤级字体/排版覆盖全局；小狼毫皮肤只认颜色）。
/// </summary>
public sealed class SchemeEditDialogViewModel : ObservableObject
{
    private readonly SchemeState _snapshot;
    private readonly ColorSchemeLibraryViewModel? _library;

    public SchemeEditDialogViewModel(ColorSchemeViewModel scheme,
        IReadOnlyCollection<SettingsFieldViewModel> fields,
        ColorSchemeLibraryViewModel? library = null, bool isSquirrel = false, bool isDraft = false)
    {
        Scheme = scheme;
        _snapshot = scheme.CloneableState;
        _library = library;
        IsSquirrel = isSquirrel;
        IsDraft = isDraft;
        Preview = new CandidatePreviewViewModel();
        Preview.AttachToScheme(fields, scheme);
    }

    public ColorSchemeViewModel Scheme { get; }

    public CandidatePreviewViewModel Preview { get; }

    /// <summary>鼠须管才显示皮肤级字体/排版组。</summary>
    public bool IsSquirrel { get; }

    /// <summary>草稿模式：编辑未入库的副本，确定=创建，取消=丢弃。</summary>
    public bool IsDraft { get; }

    public string Title => IsDraft ? "新建皮肤" : $"编辑皮肤：{Scheme.Name.Trim()}";

    /// <summary>删除/以此新增只对已入库的自定义皮肤有意义（草稿没有库身份）。</summary>
    public bool CanManage => !IsDraft && _library is not null && Scheme.CanRemove;

    /// <summary>确定：草稿 → 入库为自定义皮肤（名字撞库自动取唯一名）；已有 → 编辑已在皮肤上。</summary>
    public ColorSchemeViewModel? Commit()
    {
        if (!IsDraft) return Scheme;
        if (!Scheme.HasValidName) return null;
        return _library?.AddCustom(Scheme.CloneableState);
    }

    /// <summary>取消：已有模式还原开框快照；草稿模式直接丢弃（无操作）。</summary>
    public void Revert()
    {
        if (!IsDraft) Scheme.Restore(_snapshot);
    }

    /// <summary>对话框关闭后解开预览对外观字段的订阅。</summary>
    public void Detach() => Preview.Detach();
}
