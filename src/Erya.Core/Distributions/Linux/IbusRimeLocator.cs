namespace Erya.Core.Distributions.Linux;

/// <summary>
/// Detects ibus-rime: either the user directory already exists or the engine
/// is registered with ibus. Set <c>ERYA_IBUS_RIME_HOME</c> to point at a
/// non-standard user directory for development and CI.
/// </summary>
public sealed class IbusRimeLocator : IRimeDistributionLocator
{
    public const string UserDirEnv = "ERYA_IBUS_RIME_HOME";

    private static readonly string[] ComponentPaths =
        ["/usr/share/ibus/component/rime.xml", "/usr/local/share/ibus/component/rime.xml"];

    private static readonly string[] SharedDataDirs =
        ["/usr/share/rime-data", "/usr/local/share/rime-data", "/usr/share/rime"];

    public IRimeDistribution? Locate()
    {
        var fromEnv = Environment.GetEnvironmentVariable(UserDirEnv);
        var hasEnv = !string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv);
        var userDir = hasEnv ? fromEnv! : DefaultUserDir();

        var installed = hasEnv || Directory.Exists(userDir) || ComponentPaths.Any(File.Exists);
        if (!installed) return null;

        return new IbusRimeDistribution(
            userDir,
            SharedDataDirs.FirstOrDefault(Directory.Exists),
            ExternalTool.FindOnPath("ibus"),
            ExternalTool.FindOnPath("rime_deployer"));
    }

    private static string DefaultUserDir()
    {
        var configHome = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(configHome))
            configHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        return Path.Combine(configHome, "ibus", "rime");
    }
}
