using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya.Core.Distributions;
using Erya.ViewModels;
using Erya.Views;
using Irihi.Branding;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 关于页（IRIHI / Semi / Ursa 品牌页）的端到端检查：
/// 导航可达、品牌图标渲染、产品卡片与外链就位。
/// </summary>
public sealed class AboutPageTests
{
    private static (ShellViewModel Vm, MainWindow Window) ShowShell()
    {
        var vm = new ShellViewModel(new DistributionManager());
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (vm, window);
    }

    [AvaloniaFact]
    public void About_nav_switches_to_brand_page_with_icons_and_links()
    {
        var (vm, window) = ShowShell();

        vm.SelectedNav = vm.NavItems.Single(n => n.Key == "about");
        Dispatcher.UIThread.RunJobs();

        Assert.IsType<AboutViewModel>(vm.CurrentPage);
        Assert.Equal("关于", vm.PageTitle);

        // 品牌图标：页头 Irihi + 三张产品卡 Semi / Ursa / Lingua（Irihi.Branding 原生绘制）。
        var icons = window.GetVisualDescendants().OfType<IrihiIcon>().ToList();
        Assert.Equal(
        [
            IconName.Irihi, IconName.Semi, IconName.Ursa, IconName.Lingua,
        ], icons.Select(i => i.Icon));

        // 三张产品卡 + 致谢五行(RIME 生态) + 官网 / GitHub 两个底部链接，全部带外链。
        var links = window.GetVisualDescendants().OfType<HyperlinkButton>()
            .Select(b => b.NavigateUri?.AbsoluteUri).ToList();
        Assert.Equal(
        [
            "https://irihi.tech/products/semi-avalonia/",
            "https://irihi.tech/products/ursa",
            "https://irihi.tech/products/lingua",
            "https://github.com/rime/librime",
            "https://github.com/rime/squirrel",
            "https://github.com/rime/weasel",
            "https://github.com/yanhuacuo/rimetool",
            "https://github.com/qzly/RimeControl",
            "https://irihi.tech/",
            "https://github.com/irihitech",
        ], links);
    }

    [AvaloniaFact]
    public void About_page_renders_in_both_theme_variants()
    {
        var page = new AboutPage { DataContext = new AboutViewModel(), Width = 760, Height = 560 };
        var window = new Window { Width = 800, Height = 640, Content = page };
        window.Show();

        // IrihiIcon 内置亮/暗双调色板并跟随 ActualThemeVariant，两种变体下都要完成布局/渲染。
        foreach (var variant in new[] { ThemeVariant.Light, ThemeVariant.Dark })
        {
            Application.Current!.RequestedThemeVariant = variant;
            Dispatcher.UIThread.RunJobs();
            Assert.All(page.GetVisualDescendants().OfType<IrihiIcon>(),
                icon => Assert.Equal(
                    variant == ThemeVariant.Dark ? IconTheme.Dark : IconTheme.Light,
                    icon.ResolvedTheme));
        }

        Application.Current!.RequestedThemeVariant = ThemeVariant.Default;
        Dispatcher.UIThread.RunJobs();
        window.Close();
    }
}
