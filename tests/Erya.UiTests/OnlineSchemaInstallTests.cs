using System.IO.Compression;
using System.Formats.Tar;
using System.Net;
using System.Text;
using Erya.Core.Distributions.Linux;
using Erya.Core.Packages;
using Erya.ViewModels;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// ViewModel-level install flow: fake HTTP serves the archive, a real
/// IbusRimeDistribution points at a temp user dir. Verifies the schema list
/// refreshes after install and errors reset the row state.
/// </summary>
public sealed class OnlineSchemaInstallTests : IDisposable
{
    private readonly string _root;

    public OnlineSchemaInstallTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "erya-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private static byte[] TarGz(params (string Name, string? Content)[] entries)
    {
        var memory = new MemoryStream();
        using (var gzip = new GZipStream(memory, CompressionLevel.Fastest, leaveOpen: true))
        using (var tar = new TarWriter(gzip, TarEntryFormat.Pax, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                if (content is null)
                {
                    tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name));
                    continue;
                }
                tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, name)
                {
                    DataStream = new MemoryStream(Encoding.UTF8.GetBytes(content)),
                });
            }
        }
        return memory.ToArray();
    }

    private sealed class FixedHandler(byte[] payload) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(payload),
            });
    }

    private sealed class FailingHandler(HttpStatusCode code) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(code));
    }

    [Fact]
    public async Task Install_writes_files_refreshes_list_and_resets_row_state()
    {
        var userDir = Path.Combine(_root, "rime-home");
        Directory.CreateDirectory(userDir);
        var distribution = new IbusRimeDistribution(userDir, sharedDataDir: null,
            ibusPath: null, deployerPath: null);
        var payload = TarGz(
            ("mint-main/", null),
            ("mint-main/rime_mint.schema.yaml", "schema:\n  schema_id: rime_mint\n  name: 薄荷\n"));
        var vm = new SchemaManagerViewModel(distribution, new SchemaInstaller(new FixedHandler(payload)));

        await vm.LoadCommand.ExecuteAsync(null);
        Assert.Empty(vm.Schemas);

        // The catalog's first entry is 雾凇; its marker (rime_ice) is not in
        // this payload, so nothing gets enabled on its behalf.
        var item = vm.OnlinePackages.First();
        await vm.InstallCommand.ExecuteAsync(item);

        Assert.True(File.Exists(Path.Combine(userDir, "rime_mint.schema.yaml")));
        var mint = Assert.Single(vm.Schemas, s => s.Id == "rime_mint"); // list refreshed by the install
        Assert.False(mint.IsSelected);
        Assert.False(item.IsInstalling);
        Assert.Contains("已安装", item.Status);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task Install_enables_the_package_marker_schema_when_none_of_its_schemas_is_enabled()
    {
        var userDir = Path.Combine(_root, "rime-home");
        Directory.CreateDirectory(userDir);
        var distribution = new IbusRimeDistribution(userDir, sharedDataDir: null,
            ibusPath: null, deployerPath: null);
        var payload = TarGz(
            ("mint-main/", null),
            ("mint-main/rime_mint.schema.yaml", "schema:\n  schema_id: rime_mint\n  name: 薄荷\n"));
        var vm = new SchemaManagerViewModel(distribution, new SchemaInstaller(new FixedHandler(payload)));

        var mintPackage = vm.OnlinePackages.Single(p => p.Package.Id == "oh-my-rime");
        await vm.InstallCommand.ExecuteAsync(mintPackage);

        // Installed files alone are invisible to RIME — the marker schema goes
        // into schema_list in the same step, so the deploy that follows makes
        // it selectable right away.
        var mint = Assert.Single(vm.Schemas, s => s.Id == "rime_mint");
        Assert.True(mint.IsSelected);
        Assert.Contains("- schema: rime_mint", File.ReadAllText(Path.Combine(userDir, "default.custom.yaml")));
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task Reinstall_keeps_the_existing_schema_selection()
    {
        var userDir = Path.Combine(_root, "rime-home");
        Directory.CreateDirectory(userDir);
        File.WriteAllText(Path.Combine(userDir, "default.custom.yaml"),
            "patch:\n  schema_list:\n    - schema: luna_pinyin\n    - schema: rime_mint\n");
        var distribution = new IbusRimeDistribution(userDir, sharedDataDir: null,
            ibusPath: null, deployerPath: null);
        var payload = TarGz(
            ("mint-main/", null),
            ("mint-main/rime_mint.schema.yaml", "schema:\n  schema_id: rime_mint\n  name: 薄荷\n"));
        var vm = new SchemaManagerViewModel(distribution, new SchemaInstaller(new FixedHandler(payload)));

        var mintPackage = vm.OnlinePackages.Single(p => p.Package.Id == "oh-my-rime");
        await vm.InstallCommand.ExecuteAsync(mintPackage);

        // The user's order and selection are theirs to keep: no duplicate, no
        // reordering, luna_pinyin still first.
        var text = File.ReadAllText(Path.Combine(userDir, "default.custom.yaml"));
        Assert.Equal(1, text.Split("- schema: rime_mint").Length - 1);
        Assert.True(text.IndexOf("luna_pinyin", StringComparison.Ordinal) < text.IndexOf("rime_mint", StringComparison.Ordinal));
        Assert.True(Assert.Single(vm.Schemas, s => s.Id == "rime_mint").IsSelected);
        Assert.False(vm.HasError);
    }

    [Fact]
    public async Task Install_failure_shows_error_and_resets_row_state()
    {
        var userDir = Path.Combine(_root, "rime-home");
        Directory.CreateDirectory(userDir);
        var distribution = new IbusRimeDistribution(userDir, sharedDataDir: null,
            ibusPath: null, deployerPath: null);
        var vm = new SchemaManagerViewModel(distribution, new SchemaInstaller(new FailingHandler(HttpStatusCode.ServiceUnavailable)));

        var item = vm.OnlinePackages.First();
        await vm.InstallCommand.ExecuteAsync(item);

        Assert.True(vm.HasError);
        Assert.Contains("503", vm.ErrorMessage);
        Assert.False(item.IsInstalling);
        Assert.Null(item.Status);
    }

    [Fact]
    public void Online_packages_come_from_the_builtin_catalog()
    {
        var vm = new SchemaManagerViewModel(null);

        Assert.Equal(3, vm.OnlinePackages.Count);
        Assert.Contains(vm.OnlinePackages, p => p.Name == "雾凇拼音");
        Assert.All(vm.OnlinePackages, p => Assert.False(string.IsNullOrWhiteSpace(p.Description)));
    }
}
