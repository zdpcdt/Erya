using System.Collections.Frozen;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Erya.Core.Configuration;
using Erya.Core.Distributions;
using Erya.Core.Distributions.Weasel;
using Erya.Core.Models;
using Erya.Services;
using Erya.ViewModels;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// Empty-state onboarding: with no distribution on the machine the overview
/// offers the setup card (one-click download / official download page /
/// re-detect), and re-detection picks up a distribution installed while the
/// app is running — no restart, pages rebuild against the new frontend.
/// </summary>
public sealed class OverviewSetupTests : IDisposable
{
    private readonly string _root;

    public OverviewSetupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "erya-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private string PrefsPath => Path.Combine(_root, "preferences.json");

    private sealed class FakeLocator : IRimeDistributionLocator
    {
        public IRimeDistribution? Result;
        public IRimeDistribution? Locate() => Result;
    }

    private sealed class FakeDistribution(string id) : IRimeDistribution
    {
        public string Id => id;
        public string DisplayName => $"Fake {id}";
        public Version? Version => new(1, 0);
        public string? InstallDir => Path.Combine(Path.GetTempPath(), "fake-install");
        public string UserDir => Path.Combine(Path.GetTempPath(), "fake-rime");
        public string? UiConfigFile => null;
        public bool CanDeploy => true;
        public RimeConfigStore GetConfigStore() => new(UserDir);
        public Task DeployAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<RimeSchemaSnapshot> GetSchemasAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(new RimeSchemaSnapshot([], FrozenSet<string>.Empty));
        public Task SetSelectedSchemasAsync(IReadOnlyList<string> schemaIds, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeSetup(Func<WeaselReleaseInfo>? release = null) : IWeaselSetup
    {
        public Func<WeaselReleaseInfo> Release { get; set; } = release ?? (() => new("0.17.4", "https://example.com/weasel.exe", 12));

        public Task<WeaselReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(Release());

        public Task<string> DownloadInstallerAsync(
            WeaselReleaseInfo release, IProgress<double>? percent = null, CancellationToken cancellationToken = default)
        {
            percent?.Report(100);
            return Task.FromResult(Path.Combine(Path.GetTempPath(), "fake-installer.exe"));
        }
    }

    private MainViewModel CreateOverview(FakeLocator locator, IWeaselSetup? setup = null,
        Func<string, bool>? launcher = null)
        => new(new DistributionManager(locator), AppPreferences.Load(PrefsPath),
            setup, supportsInAppSetup: true, installerLauncher: launcher, setupPollInterval: TimeSpan.Zero);

    [AvaloniaFact]
    public void Missing_state_renders_the_setup_card_with_all_three_actions()
    {
        var shell = new ShellViewModel(new DistributionManager(new FakeLocator()),
            AppPreferences.Load(PrefsPath), weaselSetup: new FakeSetup(), supportsInAppSetup: true);
        var overview = Assert.IsType<MainViewModel>(shell.CurrentPage);

        Assert.True(overview.IsMissing);
        Assert.True(overview.SupportsInAppSetup);

        var window = new MainWindow { DataContext = shell };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Contains(buttons, b => b.Command == overview.DownloadAndInstallCommand && b.IsVisible);
        Assert.Contains(buttons, b => b.Command == overview.OpenDownloadPageCommand && b.IsVisible);
        Assert.Contains(buttons, b => b.Command == overview.RedetectCommand && b.IsVisible);
    }

    [AvaloniaFact]
    public void One_click_download_is_hidden_where_it_cannot_run()
    {
        var shell = new ShellViewModel(new DistributionManager(new FakeLocator()),
            AppPreferences.Load(PrefsPath), supportsInAppSetup: false);
        var overview = Assert.IsType<MainViewModel>(shell.CurrentPage);

        var window = new MainWindow { DataContext = shell };
        window.Show();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();

        Assert.False(overview.SupportsInAppSetup);
        var buttons = window.GetVisualDescendants().OfType<Button>().ToList();
        Assert.DoesNotContain(buttons, b => b.Command == overview.DownloadAndInstallCommand && b.IsVisible);
        // The download-page and re-detect actions remain everywhere.
        Assert.Contains(buttons, b => b.Command == overview.OpenDownloadPageCommand && b.IsVisible);
        Assert.Contains(buttons, b => b.Command == overview.RedetectCommand && b.IsVisible);
    }

    [AvaloniaFact]
    public void Redetect_picks_up_a_distribution_installed_while_running()
    {
        var locator = new FakeLocator();
        var overview = CreateOverview(locator);
        var changes = 0;
        overview.DistributionChanged += () => changes++;

        Assert.True(overview.IsMissing);

        locator.Result = new FakeDistribution("weasel");
        overview.RedetectCommand.Execute(null);

        Assert.True(overview.IsReady);
        Assert.Single(overview.AvailableDistributions);
        Assert.Equal("weasel", overview.Distribution!.Id);
        Assert.Equal(overview.Distribution.UserDir, overview.UserDir);
        Assert.Equal(1, changes);
    }

    [AvaloniaFact]
    public void Redetect_without_any_distribution_stays_missing()
    {
        var overview = CreateOverview(new FakeLocator());

        overview.RedetectCommand.Execute(null);

        Assert.True(overview.IsMissing);
        Assert.Empty(overview.AvailableDistributions);
        Assert.Null(overview.Distribution);
    }

    [AvaloniaFact]
    public async Task One_click_flow_auto_detects_after_the_installer_runs()
    {
        var locator = new FakeLocator();
        var overview = CreateOverview(locator, new FakeSetup(), launcher: _ => true);

        // The installer "runs" during the command: the frontend appears right
        // after the (faked) launcher hands off.
        locator.Result = new FakeDistribution("weasel");
        Assert.True(overview.IsMissing);

        await overview.DownloadAndInstallCommand.ExecuteAsync(null);

        Assert.True(overview.IsReady);
        Assert.Equal("weasel", overview.Distribution!.Id);
        Assert.False(overview.IsDownloadingInstaller);
        Assert.Equal("", overview.SetupStatus);
    }

    [AvaloniaFact]
    public async Task Download_failure_resets_the_busy_state()
    {
        var setup = new FakeSetup(() => throw new InvalidOperationException("boom"));
        var overview = CreateOverview(new FakeLocator(), setup, launcher: _ => true);

        await overview.DownloadAndInstallCommand.ExecuteAsync(null);

        Assert.False(overview.IsDownloadingInstaller);
        Assert.True(overview.IsMissing);
        Assert.Equal("", overview.SetupStatus);
    }

    [AvaloniaFact]
    public async Task Launcher_failure_resets_the_busy_state()
    {
        var overview = CreateOverview(new FakeLocator(), new FakeSetup(), launcher: _ => false);

        await overview.DownloadAndInstallCommand.ExecuteAsync(null);

        Assert.False(overview.IsDownloadingInstaller);
        Assert.True(overview.IsMissing);
        Assert.Equal("", overview.SetupStatus);
    }

    [AvaloniaFact]
    public async Task Poll_timeout_keeps_missing_state_with_a_hint()
    {
        var overview = CreateOverview(new FakeLocator(), new FakeSetup(), launcher: _ => true);

        await overview.DownloadAndInstallCommand.ExecuteAsync(null);

        // Installer never produced a distribution (poll interval zero → the
        // window runs out instantly); the status line tells the user what next.
        Assert.True(overview.IsMissing);
        Assert.False(overview.IsDownloadingInstaller);
        Assert.NotEqual("", overview.SetupStatus);
    }
}
