using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Erya;
using Erya.Core.Distributions;
using Erya.ViewModels;
using Erya.ViewModels.Settings;
using Ursa.Controls;
using Xunit;

namespace Erya.UiTests;

public static class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions());
}

/// <summary>
/// Ground-truth checks on the real MainWindow XAML: navigation must bind end
/// to end, and ShellViewModel must resolve nav icons from app resources.
/// Note: headless sessions do not populate App.axaml styles/resources, so the
/// icon resources are seeded by the test itself.
/// </summary>
public class ShellNavTests
{
    private static ResourceDictionary Icons(params string[] keys)
    {
        var icons = new ResourceDictionary();
        foreach (var key in keys) icons[key] = new StreamGeometry();
        return icons;
    }

    private static readonly string[] AllIconKeys =
    [
        "SemiIconHome", "SemiIconCheckList", "SemiIconSetting",
        "SemiIconColorPalette", "SemiIconCode", "SemiIconRestore",
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

        vm.SelectedNav = vm.NavItems[3]; // 外观设置
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.IsType<SettingsEditorViewModel>(vm.CurrentPage);
        Assert.Equal("外观设置", vm.PageTitle);

        // Pages are reused on revisit, no reload loop.
        vm.SelectedNav = vm.NavItems[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.SelectedNav = vm.NavItems[2];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Same(input, vm.CurrentPage);
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
        Assert.True(backup.HasHint); // no distribution in the test harness → Load ran
        Assert.Equal(2, window.GetVisualDescendants().OfType<PathPicker>().Count());

        vm.SelectedNav = vm.NavItems[0];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        vm.SelectedNav = vm.NavItems[5];
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        Assert.Same(backup, vm.CurrentPage); // page instance is reused, no reload loop
    }
}
