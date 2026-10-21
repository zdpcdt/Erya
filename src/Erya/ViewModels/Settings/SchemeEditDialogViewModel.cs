using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Erya.ViewModels.Settings;

/// <summary>
/// 皮肤编辑对话框（自定义皮肤专属）：编辑直接落在皮肤 VM 上——行内迷你预览
/// 与主窗口实时联动；对话框内的候选窗预览用 <see cref="CandidatePreviewViewModel.AttachToScheme"/>
/// 固定渲染本皮肤。「取消」由 <see cref="Revert"/> 把开框快照写回原实例。
/// </summary>
public sealed class SchemeEditDialogViewModel : ObservableObject
{
    private readonly SchemeState _snapshot;

    public SchemeEditDialogViewModel(ColorSchemeViewModel scheme, IReadOnlyCollection<SettingsFieldViewModel> fields)
    {
        Scheme = scheme;
        _snapshot = scheme.CloneableState;
        Preview = new CandidatePreviewViewModel();
        Preview.AttachToScheme(fields, scheme);
    }

    public ColorSchemeViewModel Scheme { get; }

    public CandidatePreviewViewModel Preview { get; }

    public string Title => $"编辑皮肤：{Scheme.Name.Trim()}";

    /// <summary>取消：回滚到开框快照（不换实例，绑定不断）。</summary>
    public void Revert() => Scheme.Restore(_snapshot);
}
