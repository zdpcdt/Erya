using System.Linq;
using Avalonia.Headless.XUnit;
using Erya.Core.Distributions;
using Erya.ViewModels.Advanced;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 高级设置 ground rules: the page is a plain source editor over the patch
/// file — load shows its raw text, save round-trips with YAML validation,
/// dirty tracking follows edits, and targets appear per distribution.
/// </summary>
public sealed class AdvancedSettingsTests : IDisposable
{
    private readonly string _dir;

    public AdvancedSettingsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "erya-ui-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private void Write(string fileName, string content)
        => File.WriteAllText(Path.Combine(_dir, fileName), content);

    private AdvancedSettingsViewModel Load(string? uiConfigFile = null)
    {
        var vm = new AdvancedSettingsViewModel(new StubDistribution(_dir, uiConfigFile));
        vm.LoadCommand.Execute(null);
        return vm;
    }

    [AvaloniaFact]
    public void Load_shows_patch_source_text()
    {
        Write("default.custom.yaml", "patch:\n  menu/page_size: 9\n");
        var vm = Load();

        Assert.Contains("menu/page_size: 9", vm.Document.Text);
        Assert.Contains("default.custom.yaml", vm.SourceFeedback);
        Assert.False(vm.IsDirty);
    }

    [AvaloniaFact]
    public async Task Save_round_trips_and_rejects_invalid_yaml()
    {
        Write("default.custom.yaml", "patch:\n  menu/page_size: 9\n");
        var vm = Load();

        vm.Document.Text = "patch:\n  menu/page_size: 6\n  switcher/caption: hello\n";
        await vm.SaveAndDeployCommand.ExecuteAsync(null);
        Assert.False(vm.HasError);
        Assert.Equal(6L, ReadValue("menu/page_size"));

        // Invalid YAML is refused and the file stays untouched.
        var before = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));
        vm.Document.Text = "patch: [unclosed\n";
        await vm.SaveAndDeployCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_dir, "default.custom.yaml")));
    }

    [AvaloniaFact]
    public async Task Save_creates_missing_custom_file_and_clears_dirty()
    {
        var vm = Load(); // no default.custom.yaml on disk
        Assert.Contains("尚不存在", vm.SourceFeedback);

        vm.Document.Text = "patch:\n  menu/page_size: 3\n";
        Assert.True(vm.IsDirty);

        await vm.SaveAndDeployCommand.ExecuteAsync(null);
        Assert.False(vm.IsDirty);
        Assert.True(File.Exists(Path.Combine(_dir, "default.custom.yaml")));
        Assert.Equal(3L, ReadValue("menu/page_size"));
    }

    [AvaloniaFact]
    public void Reloading_after_edits_discards_them()
    {
        Write("default.custom.yaml", "patch:\n  menu/page_size: 9\n");
        var vm = Load();

        vm.Document.Text = "patch:\n  menu/page_size: 1\n";
        Assert.True(vm.IsDirty);

        vm.LoadCommand.Execute(null);
        Assert.False(vm.IsDirty);
        Assert.Contains("menu/page_size: 9", vm.Document.Text);
    }

    [AvaloniaFact]
    public void Frontend_yaml_target_appears_only_when_available()
    {
        Assert.Single(Load().Targets, t => t.BaseFile == "default.yaml");
        Assert.DoesNotContain(Load().Targets, t => t.BaseFile == "squirrel.yaml");

        var vm = Load(uiConfigFile: "squirrel.yaml");
        Assert.Contains(vm.Targets, t => t.BaseFile == "squirrel.yaml");
    }

    private object? ReadValue(string keyPath)
    {
        var store = new Erya.Core.Configuration.RimeConfigStore(_dir);
        return store.ReadMergedValue("default.yaml", keyPath);
    }

    private sealed class StubDistribution : RimeDistributionBase
    {
        public StubDistribution(string userDir, string? uiConfigFile)
        {
            UserDir = userDir;
            UiConfigFile = uiConfigFile;
        }

        public override string Id => "stub";

        public override string DisplayName => "测试发行版";

        public override string UserDir { get; }

        public override string? UiConfigFile { get; }

        public override bool CanDeploy => false;

        public override Task DeployAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
