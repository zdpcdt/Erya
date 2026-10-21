using System.Collections;
using Avalonia;
using Avalonia.Controls;

namespace Erya.Views;

/// <summary>
/// schema 分节渲染器（字段 → Cell）：输入设置页与外观三个子页共用的列表体。
/// Sections 由调用方给（编辑器全量或按 group 过滤后的子集）。
/// </summary>
public partial class SettingsSectionsView : UserControl
{
    public static readonly StyledProperty<IEnumerable?> SectionsProperty =
        AvaloniaProperty.Register<SettingsSectionsView, IEnumerable?>(nameof(Sections));

    public SettingsSectionsView() => InitializeComponent();

    public IEnumerable? Sections
    {
        get => GetValue(SectionsProperty);
        set => SetValue(SectionsProperty, value);
    }
}
