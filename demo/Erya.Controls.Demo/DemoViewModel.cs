using CommunityToolkit.Mvvm.ComponentModel;

namespace Erya.Controls.Demo;

/// <summary>Demo 页状态：与控件无耦合，仅提供可绑定数据。</summary>
public partial class DemoViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _autoCommitAscii;

    /// <summary>int 绑定：清空输入框时产生校验错误 → ValidationErrorIcon 显示感叹号。</summary>
    [ObservableProperty]
    private int _pageSize = 5;

    [ObservableProperty]
    private string _customPhrase = "/sl";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FontPointSummary))]
    private double _fontPoint = 14;

    public string FontPointSummary => $"{FontPoint:0.#} pt";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FuzzySummary))]
    private bool _fuzzyZh = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FuzzySummary))]
    private bool _fuzzyNasal;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FuzzySummary))]
    private bool _fuzzyHF;

    public string FuzzySummary
    {
        get
        {
            var count = (FuzzyZh ? 1 : 0) + (FuzzyNasal ? 1 : 0) + (FuzzyHF ? 1 : 0);
            return count == 0 ? "未启用" : $"已启用 {count} 项";
        }
    }
}
