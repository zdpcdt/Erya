using Erya.Core.Distributions;
using Erya.Core.Distributions.Linux;
using Erya.Core.Distributions.MacOS;

namespace Erya.Core.Tests;

/// <summary>
/// Locator tests use the env overrides (same mechanism as WeaselLocator's
/// ERYA_WEASEL_DIR) so the detection logic is exercised on any dev machine,
/// not just one with the real frontend installed.
/// </summary>
public sealed class DistributionLocatorTests : IDisposable
{
    private readonly string _root;

    public DistributionLocatorTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "erya-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(SquirrelLocator.InstallDirEnv, null);
        Environment.SetEnvironmentVariable(Fcitx5RimeLocator.UserDirEnv, null);
        Environment.SetEnvironmentVariable(IbusRimeLocator.UserDirEnv, null);
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", null);
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private string Write(string relativePath, string content)
    {
        var path = Path.Combine(_root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // ---------- macOS Squirrel ----------

    [Fact]
    public async Task Squirrel_locator_locates_app_bundle_via_env_and_parses_version()
    {
        var appDir = Path.Combine(_root, "Squirrel.app");
        Write("Squirrel.app/Contents/MacOS/Squirrel", "binary");
        Write("Squirrel.app/Contents/Info.plist",
            """
            <?xml version="1.0" encoding="UTF-8"?>
            <plist version="1.0">
            <dict>
                <key>CFBundleShortVersionString</key>
                <string>0.18.0</string>
            </dict>
            </plist>
            """);
        Write("Squirrel.app/Contents/SharedSupport/rime_ice.schema.yaml",
            "schema:\n  schema_id: rime_ice\n  name: 雾凇拼音\n");
        Environment.SetEnvironmentVariable(SquirrelLocator.InstallDirEnv, appDir);

        var distribution = Assert.IsType<SquirrelDistribution>(new SquirrelLocator().Locate());

        Assert.Equal("squirrel", distribution.Id);
        Assert.Equal(new Version(0, 18, 0), distribution.Version);
        Assert.EndsWith(Path.Combine("Library", "Rime"), distribution.UserDir);
        Assert.True(distribution.CanDeploy);

        var snapshot = await distribution.GetSchemasAsync();
        Assert.Contains(snapshot.Schemas, s => s.Id == "rime_ice"); // bundled dir searched
    }

    [Fact]
    public void Squirrel_locator_ignores_env_pointing_at_a_non_app_directory()
    {
        Environment.SetEnvironmentVariable(SquirrelLocator.InstallDirEnv, _root); // no Contents/MacOS/Squirrel
        // The env dir must be ignored; on a mac that really has Squirrel the
        // locator then legitimately finds the system install, so only assert
        // that the bogus dir never wins.
        Assert.NotEqual(_root, new SquirrelLocator().Locate()?.InstallDir);
    }

    // ---------- Linux fcitx5-rime ----------

    [Fact]
    public async Task Fcitx5_locator_locates_user_dir_via_env_and_reads_schemas()
    {
        var userDir = Path.Combine(_root, "fcitx5-rime-home");
        Write("fcitx5-rime-home/luna_pinyin.schema.yaml",
            "schema:\n  schema_id: luna_pinyin\n  name: 朙月拼音\n");
        Environment.SetEnvironmentVariable(Fcitx5RimeLocator.UserDirEnv, userDir);

        var distribution = Assert.IsType<Fcitx5RimeDistribution>(new Fcitx5RimeLocator().Locate());

        Assert.Equal("fcitx5-rime", distribution.Id);
        Assert.Equal(userDir, distribution.UserDir);
        var snapshot = await distribution.GetSchemasAsync();
        Assert.Contains(snapshot.Schemas, s => s.Id == "luna_pinyin");
    }

    [Fact]
    public void Fcitx5_locator_resolves_xdg_data_home()
    {
        Write("xdgdata/fcitx5/rime/.keep", "");
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_root, "xdgdata"));

        var distribution = new Fcitx5RimeLocator().Locate();

        Assert.NotNull(distribution);
        Assert.Equal(Path.Combine(_root, "xdgdata", "fcitx5", "rime"), distribution.UserDir);
    }

    [Fact]
    public void Fcitx5_locator_returns_null_when_nothing_installed()
    {
        // On a machine that really has fcitx5-rime this cannot assert null.
        if (File.Exists("/usr/share/fcitx5/addon/rime.conf")) return;
        // Shield the real user directory too: a dev machine that has actually
        // configured fcitx5-rime carries ~/.local/share/fcitx5/rime, which
        // would otherwise count as "installed" (same disease the Squirrel
        // test above guards against on a real mac).
        Environment.SetEnvironmentVariable("XDG_DATA_HOME", Path.Combine(_root, "empty-xdg"));
        Assert.Null(new Fcitx5RimeLocator().Locate());
    }

    [Fact]
    public async Task Deploy_without_any_tool_is_reported_as_configuration_error()
    {
        var distribution = new IbusRimeDistribution(_root, sharedDataDir: null,
            ibusPath: null, deployerPath: null);

        Assert.False(distribution.CanDeploy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => distribution.DeployAsync());
    }

    [Fact]
    public async Task Fcitx5_deploy_without_deployer_is_reported_as_configuration_error()
    {
        var distribution = new Fcitx5RimeDistribution(_root, sharedDataDir: null, deployerPath: null);

        Assert.False(distribution.CanDeploy);
        await Assert.ThrowsAsync<InvalidOperationException>(() => distribution.DeployAsync());
    }

    [Fact]
    public void Fcitx5_deploy_passes_shared_data_dir_to_the_deployer()
    {
        // librime defaults the shared data dir to the working directory when
        // the second --build argument is omitted, so every schema is reported
        // missing and nothing is built (verified against librime 1.16).
        var distribution = new Fcitx5RimeDistribution(_root, sharedDataDir: "/usr/share/rime-data",
            deployerPath: "/bin/rime_deployer");

        Assert.Equal(["--build", _root, "/usr/share/rime-data"], distribution.DeployerArguments);
    }

    [Fact]
    public void Fcitx5_deploy_arguments_omit_shared_dir_only_when_it_is_unknown()
    {
        var distribution = new Fcitx5RimeDistribution(_root, sharedDataDir: null, deployerPath: "/bin/rime_deployer");

        Assert.Equal(["--build", _root], distribution.DeployerArguments);
    }

    [Fact]
    public void Ibus_deployer_fallback_passes_shared_data_dir()
    {
        var distribution = new IbusRimeDistribution(_root, sharedDataDir: "/usr/share/rime-data",
            ibusPath: null, deployerPath: "/bin/rime_deployer");

        Assert.Equal(["--build", _root, "/usr/share/rime-data"], distribution.DeployerArguments);
    }

    [Fact]
    public void Ibus_locator_locates_user_dir_via_env()
    {
        var userDir = Path.Combine(_root, "ibus-rime-home");
        Directory.CreateDirectory(userDir);
        Environment.SetEnvironmentVariable(IbusRimeLocator.UserDirEnv, userDir);

        var distribution = Assert.IsType<IbusRimeDistribution>(new IbusRimeLocator().Locate());

        Assert.Equal("ibus-rime", distribution.Id);
        Assert.Equal(userDir, distribution.UserDir);
    }

    // ---------- shared ----------

    [Fact]
    public void Path_lookup_misses_are_null()
        => Assert.Null(ExternalTool.FindOnPath("definitely-not-a-real-erya-tool"));

    [Fact]
    public void Path_lookup_finds_the_test_host_runner()
        => Assert.NotNull(ExternalTool.FindOnPath("dotnet"));
}
