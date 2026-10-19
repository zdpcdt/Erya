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
    }
}
