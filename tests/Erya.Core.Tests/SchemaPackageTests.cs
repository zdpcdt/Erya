using System.IO.Compression;
using System.Formats.Tar;
using System.Net;
using System.Text;
using Erya.Core.Packages;

namespace Erya.Core.Tests;

/// <summary>
/// Offline installer tests: the network is faked with an HttpMessageHandler
/// serving a programmatically built tar.gz, so download/unpack/backup/copy run
/// for real while the tests stay hermetic.
/// </summary>
public sealed class SchemaPackageTests : IDisposable
{
    private readonly string _root;

    public SchemaPackageTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "erya-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private string UserDir(string name = "rime-home")
    {
        var dir = Path.Combine(_root, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static SchemaPackage Package(string branch = "main", string[]? exclude = null)
        => new("mint", "薄荷测试包", "desc", "https://example.com",
            "owner", "repo", branch, exclude ?? ["others*"]);

    // ---------- catalog ----------

    [Fact]
    public void Catalog_loads_real_community_packages()
    {
        var packages = SchemaPackageCatalog.Load();

        Assert.Contains(packages, p => p.Id == "rime-ice");
        Assert.Contains(packages, p => p.Id == "rime-frost");
        Assert.Equal(packages.Select(p => p.Id), packages.Select(p => p.Id).Distinct());
        Assert.All(packages, p =>
        {
            Assert.False(string.IsNullOrWhiteSpace(p.Name));
            Assert.StartsWith("https://codeload.github.com/", p.ArchiveUrl);
            Assert.NotEmpty(p.Exclude);
        });
    }

    // ---------- wildcard ----------

    [Fact]
    public void Wildcard_star_crosses_directory_boundaries()
    {
        Assert.True(SchemaPackage.Matches(".github/workflows/ci.yml", ".git*"));
        Assert.True(SchemaPackage.Matches(".gitignore", ".git*"));
        Assert.True(SchemaPackage.Matches("docs/intro.md", "*.md"));
        Assert.True(SchemaPackage.Matches("others/essay.txt", "others*"));
        Assert.True(SchemaPackage.Matches("others.txt", "others*"));
        Assert.True(SchemaPackage.Matches("LICENSE", "LICENSE*"));

        Assert.False(SchemaPackage.Matches("cn_dicts/8105.dict.yaml", ".git*"));
        Assert.False(SchemaPackage.Matches("rime_ice.dict.yaml", "*.md"));
        Assert.False(SchemaPackage.Matches("cn_dicts/other.dict.yaml", "others*"));
        Assert.True(SchemaPackage.Matches("rime_ice.schema.yaml", "rime_ice*"));
    }

    // ---------- installer ----------

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

    private static byte[] MintPackage() => TarGz(
        ("repo-main/", null),
        ("repo-main/rime_mint.schema.yaml", "schema:\n  schema_id: rime_mint\n  name: 薄荷\n"),
        ("repo-main/cn_dicts/base.dict.yaml", "---\n...\n"),
        ("repo-main/.github/workflows/ci.yml", "ci: true\n"),
        ("repo-main/README.md", "# docs\n"),
        ("repo-main/LICENSE", "MIT\n"),
        ("repo-main/others/essay.txt", new string('词', 100)),
        ("repo-main/others_note.txt", "starts with others, still matched\n"),
        ("repo-main/custom_phrase.txt", "你好，ni hao\n"));

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
    public async Task Install_writes_rime_files_and_skips_junk()
    {
        var userDir = UserDir();
        var installer = new SchemaInstaller(new FixedHandler(MintPackage()));

        var report = await installer.InstallAsync(Package(), userDir);

        // Real config files land (nested dirs included)…
        Assert.Equal(
            "schema:\n  schema_id: rime_mint\n  name: 薄荷\n",
            File.ReadAllText(Path.Combine(userDir, "rime_mint.schema.yaml")));
        Assert.True(File.Exists(Path.Combine(userDir, "cn_dicts", "base.dict.yaml")));
        Assert.True(File.Exists(Path.Combine(userDir, "custom_phrase.txt")));
        Assert.Equal(3, report.InstalledFiles);

        // …while default excludes (.git*, *.md, LICENSE*) and package excludes
        // (others*) stay out. "others*" also catches others_note.txt — the
        // star matches anything, including "_note.txt".
        Assert.False(Directory.Exists(Path.Combine(userDir, ".github")));
        Assert.False(File.Exists(Path.Combine(userDir, "README.md")));
        Assert.False(File.Exists(Path.Combine(userDir, "LICENSE")));
        Assert.False(Directory.Exists(Path.Combine(userDir, "others")));
        Assert.False(File.Exists(Path.Combine(userDir, "others_note.txt")));

        // Fresh user dir → no backup; nothing weird to report.
        Assert.Null(report.BackupPath);
        Assert.False(report.HasSkippedFiles);
    }

    [Fact]
    public async Task Install_snapshots_existing_files_before_overwriting()
    {
        var userDir = UserDir();
        File.WriteAllText(Path.Combine(userDir, "rime_mint.schema.yaml"), "# 旧配置\n");
        var installer = new SchemaInstaller(new FixedHandler(MintPackage()));

        var report = await installer.InstallAsync(Package(), userDir);

        // Overwritten in place…
        Assert.DoesNotContain("旧配置", File.ReadAllText(Path.Combine(userDir, "rime_mint.schema.yaml")));
        // …and the pre-install snapshot keeps the old version for rollback.
        Assert.NotNull(report.BackupPath);
        Assert.True(File.Exists(report.BackupPath));
        using (var archive = ZipFile.OpenRead(report.BackupPath!))
        {
            var old = archive.GetEntry("rime_mint.schema.yaml");
            Assert.NotNull(old);
            using var reader = new StreamReader(old!.Open());
            Assert.Contains("旧配置", reader.ReadToEnd());
        }
        Assert.StartsWith(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Erya"),
            report.BackupPath);
    }

    [Fact]
    public async Task Install_rejects_packages_without_any_schema()
    {
        var payload = TarGz(("repo-main/", null), ("repo-main/dict.yaml", "just a dict\n"));
        var installer = new SchemaInstaller(new FixedHandler(payload));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => installer.InstallAsync(Package(), UserDir()));

        Assert.Contains("schema.yaml", error.Message);
    }

    [Fact]
    public async Task Install_skips_entries_escaping_the_archive_root()
    {
        var payload = TarGz(
            ("repo-main/", null),
            ("repo-main/rime_mint.schema.yaml", "schema:\n  schema_id: rime_mint\n"),
            ("repo-main/../outside.txt", "evil\n"),
            ("repo-main/sub/../../escape.txt", "evil\n"));
        var installer = new SchemaInstaller(new FixedHandler(payload));
        var userDir = UserDir();

        var report = await installer.InstallAsync(Package(), userDir);

        Assert.Equal(1, report.InstalledFiles);
        Assert.Equal(2, report.Skipped.Count);
        Assert.False(File.Exists(Path.Combine(_root, "outside.txt")));
        Assert.False(File.Exists(Path.Combine(_root, "escape.txt")));
    }

    [Fact]
    public async Task Download_failures_surface_as_friendly_errors()
    {
        var installer = new SchemaInstaller(new FailingHandler(HttpStatusCode.NotFound));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => installer.InstallAsync(Package(), UserDir()));

        Assert.Contains("HTTP 404", error.Message);
    }

    [Fact]
    public async Task Install_reports_download_progress_with_percent()
    {
        var phases = new List<SchemaInstallProgress>();
        var progress = new Progress<SchemaInstallProgress>(phases.Add);
        var installer = new SchemaInstaller(new FixedHandler(MintPackage()));

        await installer.InstallAsync(Package(), UserDir(), progress);
        await Task.Delay(50); // Progress<T> posts asynchronously

        Assert.Contains(phases, p => p.Percent is >= 100);
        Assert.Contains(phases, p => p.Phase.Contains("写入"));
    }
}
