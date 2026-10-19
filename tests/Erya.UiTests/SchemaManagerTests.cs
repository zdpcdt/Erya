using System.Linq;
using Avalonia.Headless.XUnit;
using Erya.Core.Distributions;
using Erya.ViewModels;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// 方案管理：顺序调整由拖拽（ReorderableItemsControl 直接 Move 集合）完成，
/// 保存时按集合顺序取已勾选 id —— 未勾选行的位置不落盘；添加新方案创建
/// 脚手架并预勾选（保存仍需显式点击）。
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
    public async Task Reordered_collection_saves_selected_order_and_ignores_unchecked_rows()
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

        var b = vm.Schemas.Single(s => s.Id == "b");
        Assert.False(b.IsSelected);

        // 拖拽即对集合自由 Move：把 c 拖到最前；未勾选的 b 拖到末尾不影响落盘顺序。
        var c = vm.Schemas.Single(s => s.Id == "c");
        vm.Schemas.Move(vm.Schemas.IndexOf(c), 0);
        vm.Schemas.Move(vm.Schemas.IndexOf(b), vm.Schemas.Count - 1);

        await vm.SaveAndDeployCommand.ExecuteAsync(null);
        var saved = File.ReadAllText(Path.Combine(_dir, "default.custom.yaml"));
        Assert.DoesNotContain("- schema: b", saved);
        Assert.Equal(["c", "a"], new Erya.Core.Configuration.RimeSchemaCatalog(_dir).ReadSelectedIds());
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
