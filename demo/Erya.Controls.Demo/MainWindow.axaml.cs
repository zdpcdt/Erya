using Avalonia.Controls;

namespace Erya.Controls.Demo;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        // Demo 的所有绑定都指向 DemoViewModel；此前从未设置 DataContext，
        // 绑定静默失败、控件显示默认值（列表则为空）。
        DataContext = new DemoViewModel();
        var vm = (DemoViewModel)DataContext;

        // SearchablePicker 是「点一下即回调」语义：写回与收起都是宿主的事，
        // demo 里直接落到 VM（完整形态入列重建候选，刷新「已添加」标记）。
        BasicPicker.EntryPicked += (_, e) => vm.BasicPicked = "选中：" + e.Entry.Id;
        FullPicker.EntryPicked += (_, e) => vm.Pick(e.Entry.Id);
        FullPicker.CustomEntryAdded += (_, e) => vm.Pick(e.Id);
    }
}
