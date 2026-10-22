using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya;
using Erya.Core.Distributions;
using Erya.ViewModels;
using Erya.ViewModels.Advanced;
using Erya.ViewModels.Settings;
using Ursa.Controls;
using Xunit;

// 挂上真正的 Erya App：App.axaml 的资源与样式（Semi 主题、图标、Cell 主题）
// 才会在 headless 会话中生效，控件模板才能套用。
[assembly: AvaloniaTestApplication(typeof(Erya.UiTests.TestAppBuilder))]

namespace Erya.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            // UseHeadlessDrawing=false + UseSkia：走真实 CPU Skia 渲染，测试里才能
            // 用 RenderTargetBitmap 导出控件截图做视觉验收（Skia 同时提供字体管理器）。
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}

/// <summary>
/// Ground-truth checks on the real MainWindow XAML: navigation must bind end
/// to end, and ShellViewModel must resolve nav icons from app resources.
/// Note: headless sessions do not populate App.axaml styles/resources, so the
/// icon resources are seeded by the test itself.
/// </summary>
public class ShellNavTests
{
    public static ResourceDictionary IconDictionary(params string[] keys)
    {
        var icons = new ResourceDictionary();
        foreach (var key in keys) icons[key] = new StreamGeometry();
        return icons;
    }

    private static ResourceDictionary Icons(params string[] keys) => IconDictionary(keys);

    private static readonly string[] AllIconKeys =
    [
        "SemiIconHome", "SemiIconCheckList", "SemiIconSetting",
        "SemiIconColorPalette", "SemiIconCode", "SemiIconRestore", "SemiIconDesktop",
        "SemiIconInfoCircle",
    ];

    [AvaloniaFact]
    public void Nav_selection_binds_and_icons_resolve()
    {
        Application.Current!.Resources.MergedDictionaries.Add(Icons(AllIconKeys));

        var vm = new ShellViewModel(new DistributionManager());
        Assert.All(vm.NavItems, i => Assert.IsAssignableFrom<Geometry>(i.Icon));

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var nav = window.GetVisualDescendants().OfType<NavMenu>().Single();
        Assert.Equal(vm.NavItems[0], nav.SelectedItem); // initial selection pushed VM -> control

        vm.SelectedNav = vm.NavItems[1];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Equal(vm.NavItems[1], nav.SelectedItem); // control selection follows the VM
        Assert.IsType<SchemaManagerViewModel>(vm.CurrentPage);
        Assert.Equal("方案管理", vm.PageTitle);
    }

    [AvaloniaFact]
    public void Settings_pages_lazy_load_and_render_from_schema()
    {
        Application.Current!.Resources.MergedDictionaries.Add(Icons(AllIconKeys));

        var vm = new ShellViewModel(new DistributionManager());
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        // No distribution in the headless harness → page loads with a hint, no fields.
        vm.SelectedNav = vm.NavItems[2]; // 输入设置
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var input = Assert.IsType<SettingsEditorViewModel>(vm.CurrentPage);
        Assert.Equal("输入设置", vm.PageTitle);
        Assert.False(input.IsSupported); // no RIME frontend in the test harness

        vm.SelectedNav = vm.NavItems[3]; // 外观设置是分组：点它不换页（页面在子项上）
        Dispatcher.UIThread.RunJobs();
        Assert.Same(input, vm.CurrentPage);
        Assert.Equal("输入设置", vm.PageTitle);

        // 子项逐个导航：皮肤库 / 候选窗排版 / 应用行为共享同一外观编辑器。
        NavItem Child(string key) => vm.NavItems.Single(n => n.Key == "appearance").Children!.Single(c => c.Key == key);
        var appearanceParent = vm.NavItems[3];
        Assert.Equal(["skin", "layout", "apps"], appearanceParent.Children!.Select(c => c.Key).ToList());

        vm.SelectedNav = Child("skin");
        Dispatcher.UIThread.RunJobs();
        var skin = Assert.IsType<SkinPageViewModel>(vm.CurrentPage);
        Assert.Equal("皮肤库", vm.PageTitle);
        vm.SelectedNav = Child("layout");
        Dispatcher.UIThread.RunJobs();
        var layout = Assert.IsType<LayoutPageViewModel>(vm.CurrentPage);
        Assert.Same(skin.Editor, layout.Editor); // 三页共享一份编辑器
        vm.SelectedNav = Child("apps");
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<AppsPageViewModel>(vm.CurrentPage);

        // Pages are reused on revisit, no reload loop.
        vm.SelectedNav = vm.NavItems[0];
        Dispatcher.UIThread.RunJobs();
        vm.SelectedNav = vm.NavItems[2];
        Dispatcher.UIThread.RunJobs();
        Assert.Same(input, vm.CurrentPage);
    }

    [AvaloniaFact]
    public void Appearance_nav_group_hidden_stays_flat_and_navigable()
    {
        Application.Current!.Resources.MergedDictionaries.Add(Icons(AllIconKeys));

        // Linux 运行时由 App.axaml.cs 传入 false：外观三页编辑的 UI YAML
        // （squirrel/weasel）在 fcitx5/ibus 前端上不存在。
        var vm = new ShellViewModel(new DistributionManager(), showAppearance: false);
        Assert.Equal(["overview", "schemas", "inputSettings", "advanced", "backup", "about"],
            vm.NavItems.Select(n => n.Key).ToList());

        var window = new MainWindow { DataContext = vm };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // 分组拿掉后导航保持扁平可用：输入设置之后直接是高级设置、备份恢复。
        vm.SelectedNav = vm.NavItems[3];
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<AdvancedSettingsViewModel>(vm.CurrentPage);
        vm.SelectedNav = vm.NavItems[4];
        Dispatcher.UIThread.RunJobs();
        Assert.IsType<BackupRestoreViewModel>(vm.CurrentPage);
        Assert.Equal("备份恢复", vm.PageTitle);
    }

    [AvaloniaFact]
    public void Backup_nav_switches_page_and_lazy_loads_once()
    {
        Application.Current!.Resources.MergedDictionaries.Add(Icons(AllIconKeys));

        var vm = new ShellViewModel(new DistributionManager());
        var window = new MainWindow { DataContext = vm };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        vm.SelectedNav = vm.NavItems[5];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var backup = Assert.IsType<BackupRestoreViewModel>(vm.CurrentPage);
        Assert.Equal("备份恢复", vm.PageTitle);
        Assert.False(backup.HasUserDir); // no distribution in the test harness → Load ran, empty state
        // 重构后的页面：零 Banner、零 PathPicker（文件选择器移到 code-behind）。
        Assert.Empty(window.GetVisualDescendants().OfType<Banner>());
        Assert.Empty(window.GetVisualDescendants().OfType<PathPicker>());

        vm.SelectedNav = vm.NavItems[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.SelectedNav = vm.NavItems[5];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Same(backup, vm.CurrentPage); // page instance is reused, no reload loop
    }
}
