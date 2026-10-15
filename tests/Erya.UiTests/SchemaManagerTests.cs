using System.Linq;
using Avalonia.Headless.XUnit;
using Erya.Core.Distributions;
using Erya.ViewModels;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 方案管理：启用顺序的调整只作用于已启用方案（跳过中间未勾选的行），
/// 添加新方案创建脚手架并预勾选（保存仍需显式点击）。
/// </summary>
public sealed class SchemaManagerTests : IDisposable
{
    private readonly string _dir;

    public SchemaManagerTests()
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

    private (SchemaManagerViewModel Vm, StubDistribution Dist) Load()
    {
        var dist = new StubDistribution(_dir);
        var vm = new SchemaManagerViewModel(dist);
        vm.LoadCommand.Execute(null);
        return (vm, dist);
    }

    [AvaloniaFact]
    public async Task Moving_swaps_only_among_selected_schemas()
    {
        Write("default.yaml", """
            schema_list:
              - schema: a
              - schema: b
              - schema: c
            """);
        Write("b.schema.yaml", "schema:\n  schema_id: b\n  name: B\n");
        // b stays disabled; selected order is a, c.
        Write("default.custom.yaml", "patch:\n  schema_list:\n    - schema: a\n    - schema: c\n");
        var (vm, dist) = Load();

        var a = vm.Schemas.Single(s => s.Id == "a");
        var c = vm.Schemas.Single(s => s.Id == "c");
        Assert.False(vm.Schemas.Single(s => s.Id == "b").IsSelected);

        // Move c up: it lands before a, skipping past the unchecked b.
        vm.MoveDownCommand.Execute(a); // no-op at the top of the selected set
        vm.MoveUpCommand.Execute(c);

        await vm.SaveAndDeployCommand.ExecuteAsync(null);
        var saved = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));
        Assert.Contains("- schema: c", saved);
        Assert.Equal(["c", "a"], new Erya.Core.Configuration.RimeSchemaCatalog(_dir).ReadSelectedIds());
    }

    [AvaloniaFact]
    public async Task Move_at_the_edges_of_the_selected_set_is_a_no_op()
    {
        Write("default.custom.yaml", "patch:\n  schema_list:\n    - schema: a\n    - schema: c\n");
        Write("a.schema.yaml", "schema:\n  schema_id: a\n  name: A\n");
        Write("c.schema.yaml", "schema:\n  schema_id: c\n  name: C\n");
        var (vm, _) = Load();

        var first = vm.Schemas.First(s => s.IsSelected);
        var last = vm.Schemas.Last(s => s.IsSelected);
        vm.MoveUpCommand.Execute(first);
        vm.MoveDownCommand.Execute(last);

        // Unselected rows never receive the move (command guards), and the
        // selected order is unchanged.
        Assert.Equal(["a", "c"], vm.Schemas.Where(s => s.IsSelected).Select(s => s.Id));
    }

    [AvaloniaFact]
    public async Task Adding_a_schema_creates_the_stub_and_preselects_it()
    {
        Write("default.yaml", "schema_list:\n  - schema: luna_pinyin\n");
        var (vm, dist) = Load();

        vm.NewSchemaId = "my_pinyin";
        vm.NewSchemaName = "我的拼音";
        await vm.AddSchemaCommand.ExecuteAsync(null);

        Assert.True(File.Exists(Path.Combine(_dir, "my_pinyin.schema.yaml")));
        var created = vm.Schemas.Single(s => s.Id == "my_pinyin");
        Assert.Equal("我的拼音", created.Name);
        Assert.True(created.IsSelected); // offered pre-checked, saving is explicit
        Assert.Equal("", vm.NewSchemaId); // form cleared

        // Duplicate creation surfaces a readable error and writes nothing new.
        vm.NewSchemaId = "my_pinyin";
        await vm.AddSchemaCommand.ExecuteAsync(null);
        Assert.True(vm.HasError);
        Assert.Contains("已存在", vm.ErrorMessage);
    }

    private sealed class StubDistribution : RimeDistributionBase
    {
        public StubDistribution(string userDir) => UserDir = userDir;

        public override string Id => "stub";

        public override string DisplayName => "测试发行版";

        public override string UserDir { get; }

        public override bool CanDeploy => false;

        public override Task DeployAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }
}
