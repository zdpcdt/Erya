using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Erya.ViewModels.Settings;

/// <summary>
/// 皮肤编辑对话框：编辑直接落在皮肤 VM 上（预览实时联动）。内置皮肤同样可调，
/// 但「确定」时经 <see cref="Commit"/> 另存为新的自定义皮肤（原内置皮肤还原
/// 快照）；自定义皮肤「确定」保留编辑，「取消」一律还原快照。字体与排版组只在
/// 鼠须管下显示（它支持皮肤级字体/排版覆盖全局；小狼毫皮肤只认颜色）。
/// </summary>
public sealed class SchemeEditDialogViewModel : ObservableObject
{
    private readonly SchemeState _snapshot;
    private readonly ColorSchemeLibraryViewModel? _library;

    public SchemeEditDialogViewModel(ColorSchemeViewModel scheme,
        IReadOnlyCollection<SettingsFieldViewModel> fields,
        ColorSchemeLibraryViewModel? library = null, bool isSquirrel = false)
    {
        Scheme = scheme;
        _snapshot = scheme.CloneableState;
        _library = library;
        IsSquirrel = isSquirrel;
        Preview = new CandidatePreviewViewModel();
        Preview.AttachToScheme(fields, scheme);
    }

    public ColorSchemeViewModel Scheme { get; }

    public CandidatePreviewViewModel Preview { get; }

    /// <summary>鼠须管才显示皮肤级字体/排版组。</summary>
    public bool IsSquirrel { get; }

    /// <summary>内置皮肤：改完只能另存为新的自定义皮肤。</summary>
    public bool SavesAsCopy => Scheme.IsShipped && _library is not null;

    public string Title => SavesAsCopy
        ? $"调整皮肤：{Scheme.Name.Trim()}"
        : $"编辑皮肤：{Scheme.Name.Trim()}";

    public string Tip => SavesAsCopy
        ? "这是内置皮肤：调整效果后点「确定」将另存为一个新的自定义皮肤（内置皮肤保持原样）。"
        : "修改实时预览；「确定」保留，「取消」还原。";

    /// <summary>确定：内置 → 另存为副本并还原原皮肤；自定义 → 编辑已在皮肤上。</summary>
    public ColorSchemeViewModel? Commit()
    {
        if (!SavesAsCopy) return Scheme;
        return _library!.SaveEditedAsCustom(Scheme, _snapshot);
    }

    /// <summary>取消/关闭：全部还原开框快照（不换实例，绑定不断）。</summary>
    public void Revert() => Scheme.Restore(_snapshot);

    /// <summary>对话框关闭后解开预览对外观字段的订阅。</summary>
    public void Detach() => Preview.Detach();
}
