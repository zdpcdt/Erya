using System.Linq;
using Avalonia.Headless.XUnit;
using Erya.Core.Distributions;
using Erya.ViewModels.Advanced;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 高级设置 ground rules: the tree shows merged values with patch badges,
/// inline scalar edits write patch keys, per-node undo removes them, filters
/// narrow the tree, and source mode round-trips the raw file with validation.
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
        var distribution = new StubDistribution(_dir, uiConfigFile);
        var vm = new AdvancedSettingsViewModel(distribution);
        vm.LoadCommand.Execute(null);
        return vm;
    }

    private static ConfigNodeViewModel? Find(AdvancedSettingsViewModel vm, string path)
        => vm.Roots.SelectMany(Walk).FirstOrDefault(n => n.Path == path);

    private static System.Collections.Generic.IEnumerable<ConfigNodeViewModel> Walk(ConfigNodeViewModel node)
    {
        yield return node;
        foreach (var child in node.Children.SelectMany(Walk)) yield return child;
    }

    [AvaloniaFact]
    public void Tree_shows_merged_values_and_patch_badges()
    {
        Write("default.yaml", """
            menu:
              page_size: 5
            switcher:
              caption: 方案選單
            """);
        Write("default.custom.yaml", "patch:\n  menu/page_size: 9\n");

        var vm = Load();
        Assert.True(vm.HasTree);
        Assert.Equal(2, vm.Roots.Count); // menu + switcher

        var pageSize = Find(vm, "menu/page_size");
        Assert.NotNull(pageSize);
        Assert.Equal("9", pageSize.Preview);        // merged value
        Assert.True(pageSize.IsModified);           // badge: patched

        var caption = Find(vm, "switcher/caption");
        Assert.Equal("方案選單", caption!.Preview);
        Assert.False(caption.IsModified);
    }

    [AvaloniaFact]
    public async Task Inline_edit_writes_patch_and_undo_removes_it()
    {
        Write("default.yaml", "menu:\n  page_size: 5\n");
        var vm = Load();

        var pageSize = Find(vm, "menu/page_size")!;
        Assert.True(pageSize.IsEditable);

        vm.BeginEditCommand.Execute(pageSize);
        pageSize.EditText = "7";
        await vm.ApplyEditCommand.ExecuteAsync(pageSize);

        Assert.Contains("menu/page_size: 7", File.ReadAllText(Path.Combine(_dir, "default.custom.yaml")));
        Assert.True(Find(vm, "menu/page_size")!.IsModified);
        Assert.Equal("7", Find(vm, "menu/page_size")!.Preview);

        await vm.UndoNodePatchCommand.ExecuteAsync(pageSize);
        Assert.False(Find(vm, "menu/page_size")!.IsModified);
        Assert.Equal("5", Find(vm, "menu/page_size")!.Preview);
        // The patch file survives (other keys may remain); only the key is gone.
        Assert.DoesNotContain("page_size", File.ReadAllText(Path.Combine(_dir, "default.custom.yaml")));
    }

    [AvaloniaFact]
    public void Search_and_only_modified_filters_narrow_the_tree()
    {
        Write("default.yaml", """
            menu:
              page_size: 5
            switcher:
              caption: abc
            """);
        Write("default.custom.yaml", "patch:\n  switcher/caption: xyz\n");
        var vm = Load();

        // Only modified: the switcher branch survives, menu does not.
        vm.OnlyModified = true;
        Assert.True(Find(vm, "switcher/caption")!.IsVisible);
        Assert.False(Find(vm, "menu/page_size")!.IsVisible);
        Assert.True(Find(vm, "switcher")!.IsVisible); // ancestor of a match stays

        // Search by value: "xyz" matches the patched caption.
        vm.OnlyModified = false;
        vm.SearchText = "xyz";
        Assert.True(Find(vm, "switcher/caption")!.IsVisible);
        Assert.False(Find(vm, "menu/page_size")!.IsVisible);

        vm.SearchText = "";
        Assert.True(Find(vm, "menu/page_size")!.IsVisible);
    }

    [AvaloniaFact]
    public async Task Source_mode_round_trips_and_rejects_invalid_yaml()
    {
        Write("default.custom.yaml", "patch:\n  menu/page_size: 9\n");
        var vm = Load();

        vm.IsSourceMode = true;
        Assert.Contains("menu/page_size: 9", vm.SourceText);

        vm.SourceText = "patch:\n  menu/page_size: 6\n  switcher/caption: hello\n";
        await vm.SaveSourceCommand.ExecuteAsync(null);
        Assert.Equal(6L, ReadValue("menu/page_size"));

        // Invalid YAML is refused and the file stays untouched.
        var before = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));
        vm.SourceText = "patch: [unclosed\n";
        await vm.SaveSourceCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.Equal(before, File.ReadAllText(Path.Combine(_dir, "default.custom.yaml")));
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
