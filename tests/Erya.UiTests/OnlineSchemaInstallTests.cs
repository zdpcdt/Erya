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

        var item = vm.OnlinePackages.First();
        await vm.InstallCommand.ExecuteAsync(item);

        Assert.True(File.Exists(Path.Combine(userDir, "rime_mint.schema.yaml")));
        Assert.Contains(vm.Schemas, s => s.Id == "rime_mint"); // list refreshed by the install
        Assert.False(item.IsInstalling);
        Assert.Contains("已安装", item.Status);
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
