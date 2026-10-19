using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Erya.Core.Distributions;
using Erya.Core.Distributions.Linux;
using Erya.Services;
using Erya.ViewModels;
using Erya.ViewModels.Settings;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// Frontend switcher: on a machine carrying both fcitx5-rime and ibus-rime
/// (faked via the ERYA_*_HOME env overrides), the selector shows up, picking
/// a frontend rebuilds the pages against it, and the choice persists.
/// </summary>
public sealed class DistributionSwitchTests : IDisposable
{
    private readonly string _root;
    private readonly string _fcitx5Dir;
    private readonly string _ibusDir;
    private readonly string _prefsPath;

    public DistributionSwitchTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "erya-tests", Guid.NewGuid().ToString("N"));
        _fcitx5Dir = Path.Combine(_root, "fcitx5");
        _ibusDir = Path.Combine(_root, "ibus");
        Directory.CreateDirectory(_fcitx5Dir);
        Directory.CreateDirectory(_ibusDir);
        _prefsPath = Path.Combine(_root, "preferences.json");
        // Shield the real fcitx5 user dir so detection stays hermetic on a
        // machine that actually uses fcitx5-rime (same trick as Core tests).
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_root, "empty-xdg"));
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(Fcitx5RimeLocator.UserDirEnv, null);
        Environment.SetEnvironmentVariable(IbusRimeLocator.UserDirEnv, null);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private ShellViewModel CreateShell()
    {
        Environment.SetEnvironmentVariable(Fcitx5RimeLocator.UserDirEnv, _fcitx5Dir);
        Environment.SetEnvironmentVariable(IbusRimeLocator.UserDirEnv, _ibusDir);
        return new ShellViewModel(
            new DistributionManager(new Fcitx5RimeLocator(), new IbusRimeLocator()),
            AppPreferences.Load(_prefsPath));
    }

    [AvaloniaFact]
    public void Both_frontends_available_selector_visible_fcitx5_wins_by_default()
    {
        var shell = CreateShell();
        var overview = Assert.IsType<MainViewModel>(shell.CurrentPage);

        Assert.True(overview.HasMultipleDistributions);
        Assert.Equal(["fcitx5-rime", "ibus-rime"], overview.AvailableDistributions.Select(d => d.Id));
        Assert.Equal("fcitx5-rime", overview.Distribution!.Id); // locator order is the default
        Assert.Equal(_fcitx5Dir, overview.UserDir);
    }

    [AvaloniaFact]
    public void Switching_frontend_rebuilds_pages_against_the_new_distribution()
    {
        var shell = CreateShell();
        shell.SelectedNav = shell.NavItems.First(n => n.Key == "schemas");
        var schemasBefore = Assert.IsType<SchemaManagerViewModel>(shell.CurrentPage);

        shell.SelectedNav = shell.NavItems[0]; // back to overview
        var overview = Assert.IsType<MainViewModel>(shell.CurrentPage);
        overview.Distribution = overview.AvailableDistributions.Single(d => d.Id == "ibus-rime");

        Assert.Equal("ibus-rime", overview.Distribution.Id);
        Assert.Equal(_ibusDir, overview.UserDir);

        // The schema page the user returns to is a fresh instance bound to
        // the new frontend; unsaved edits on other pages are gone with it.
        shell.SelectedNav = shell.NavItems.First(n => n.Key == "schemas");
        var schemasAfter = Assert.IsType<SchemaManagerViewModel>(shell.CurrentPage);
        Assert.NotSame(schemasBefore, schemasAfter);
    }

    [AvaloniaFact]
    public void Choice_persists_and_is_honored_on_next_launch()
    {
        var shell = CreateShell();
        var overview = Assert.IsType<MainViewModel>(shell.CurrentPage);
        overview.Distribution = overview.AvailableDistributions.Single(d => d.Id == "ibus-rime");
        Assert.True(File.Exists(_prefsPath));

        // Next launch reads the persisted choice: ibus-rime despite fcitx5
        // winning the locator order.
        var shell2 = CreateShell();
        var overview2 = Assert.IsType<MainViewModel>(shell2.CurrentPage);
        Assert.Equal("ibus-rime", overview2.Distribution!.Id);
    }

    [AvaloniaFact]
    public void Single_frontend_keeps_the_selector_hidden_and_writes_no_preferences()
    {
        Environment.SetEnvironmentVariable(IbusRimeLocator.UserDirEnv, _ibusDir);
        var shell = new ShellViewModel(
            new DistributionManager(new Fcitx5RimeLocator(), new IbusRimeLocator()),
            AppPreferences.Load(_prefsPath));

        var overview = Assert.IsType<MainViewModel>(shell.CurrentPage);
        Assert.False(overview.HasMultipleDistributions);
        Assert.Equal("ibus-rime", overview.Distribution!.Id);
        Assert.False(File.Exists(_prefsPath)); // auto-detection is never persisted
    }

    [AvaloniaFact]
    public void Overview_renders_the_frontend_selector_when_multiple_frontends_exist()
    {
        var shell = CreateShell();

        var window = new MainWindow { DataContext = shell };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var selector = window.GetVisualDescendants().OfType<ComboBox>()
            .Single(c => (c.ItemsSource as System.Collections.IEnumerable)?.Cast<object>().Count() == 2);
        Assert.True(selector.IsVisible);
    }
}
