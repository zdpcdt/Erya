using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Erya;
using Erya.Core.Distributions;
using Erya.ViewModels;
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
    [AvaloniaFact]
    public void Nav_selection_binds_and_icons_resolve()
    {
        var icons = new ResourceDictionary
        {
            ["SemiIconHome"] = new StreamGeometry(),
            ["SemiIconCheckList"] = new StreamGeometry(),
            ["SemiIconRestore"] = new StreamGeometry(),
        };
        Application.Current!.Resources.MergedDictionaries.Add(icons);

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
}
