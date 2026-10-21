using System;
using System.IO;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya.Core.Distributions;
using Erya.ViewModels;
using Erya.ViewModels.Settings;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 生成 README / 参赛材料用的应用截图：真实 MainWindow + 真实发行版配置。
/// 只在本机检测到 RIME 发行版时产出（否则静默跳过，CI 上不炸）；
/// 图写入仓库 docs/screenshots，随源码一起提交。
/// </summary>
public sealed class DocsScreenshotGenerator
{
    [AvaloniaFact]
    public void Generate_screenshots()
    {
        // 与 App.axaml.cs 相同的定位器组合（截图机就是真实运行时）
        var manager = OperatingSystem.IsWindows()
            ? new DistributionManager(new Erya.Core.Distributions.Weasel.WeaselLocator())
            : OperatingSystem.IsMacOS()
                ? new DistributionManager(new Erya.Core.Distributions.MacOS.SquirrelLocator())
                : new DistributionManager(new Erya.Core.Distributions.Linux.Fcitx5RimeLocator(),
                    new Erya.Core.Distributions.Linux.IbusRimeLocator());
        var vm = new ShellViewModel(manager);
        if (vm.CurrentPage is not MainViewModel { Distribution: not null }) return; // 没装 RIME 的机器：不产出

        var dir = RepoScreenshotsDir();
        Directory.CreateDirectory(dir);

        var window = new MainWindow { DataContext = vm, Width = 1180, Height = 780,
                                      WindowStartupLocation = WindowStartupLocation.Manual };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        Capture(vm, window, "overview.png", ThemeVariant.Light);

        vm.SelectedNav = vm.NavItems[1]; // 方案管理
        Dispatcher.UIThread.RunJobs();
        Capture(vm, window, "schemas.png", ThemeVariant.Light);

        vm.SelectedNav = vm.NavItems[2]; // 输入设置
        Dispatcher.UIThread.RunJobs();
        ExpandDetailRows(window);
        Capture(vm, window, "input.png", ThemeVariant.Light);

        // 外观设置分组 → 三个子页。
        NavItem Child(string key) => vm.NavItems.Single(n => n.Key == "appearance").Children!.Single(c => c.Key == key);

        vm.SelectedNav = Child("skin"); // 皮肤库（master-detail）
        Dispatcher.UIThread.RunJobs();
        Capture(vm, window, "skin.png", ThemeVariant.Light);
        Capture(vm, window, "skin-dark.png", ThemeVariant.Dark);

        vm.SelectedNav = Child("layout"); // 候选窗排版
        Dispatcher.UIThread.RunJobs();
        Capture(vm, window, "layout.png", ThemeVariant.Light);

        vm.SelectedNav = Child("apps"); // 应用行为
        Dispatcher.UIThread.RunJobs();
        Capture(vm, window, "apps.png", ThemeVariant.Light);

        vm.SelectedNav = vm.NavItems[4]; // 高级设置
        Dispatcher.UIThread.RunJobs();
        Capture(vm, window, "advanced.png", ThemeVariant.Light);

        vm.SelectedNav = vm.NavItems[5]; // 备份恢复
        Dispatcher.UIThread.RunJobs();
        Capture(vm, window, "backup.png", ThemeVariant.Light);

        window.Close();
    }

    /// <summary>键位映射等多选行展开一点，画面信息量更高。</summary>
    private static void ExpandDetailRows(Window window)
    {
        foreach (var cell in window.GetVisualDescendants().OfType<Erya.Controls.Cell>()
                     .Where(c => c.Detail is not null).Take(3))
        {
            cell.IsExpanded = true;
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static void Capture(ShellViewModel vm, Window window, string name, ThemeVariant variant)
    {
        Application.Current!.RequestedThemeVariant = variant;
        Dispatcher.UIThread.RunJobs();
        // 让懒加载页（异步 Load）落地
        Dispatcher.UIThread.RunJobs();
        window.CaptureRenderedFrame()?.Save(Path.Combine(RepoScreenshotsDir(), name));
        Application.Current.RequestedThemeVariant = ThemeVariant.Default;
        Dispatcher.UIThread.RunJobs();
    }

    private static string RepoScreenshotsDir()
    {
        // bin/Debug/net10.0 → 仓库根（tests/Erya.UiTests/bin/Debug/net10.0 共 5 层）
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 5 && dir.Parent is not null; i++) dir = dir.Parent;
        return Path.Combine(dir.FullName, "docs", "screenshots");
    }
}
