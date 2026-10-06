namespace Erya.Core.Distributions.Linux;

/// <summary>
/// Detects fcitx5-rime: either the user directory already exists or the
/// fcitx5 addon is registered. Set <c>ERYA_FCITX5_RIME_HOME</c> to point at a
/// non-standard user directory for development and CI.
/// </summary>
public sealed class Fcitx5RimeLocator : IRimeDistributionLocator
{
    public const string UserDirEnv = "ERYA_FCITX5_RIME_HOME";

    private static readonly string[] AddonConfPaths =
        ["/usr/share/fcitx5/addon/rime.conf", "/usr/local/share/fcitx5/addon/rime.conf"];

    private static readonly string[] SharedDataDirs =
        ["/usr/share/rime-data", "/usr/local/share/rime-data", "/usr/share/rime"];

    public IRimeDistribution? Locate()
    {
        var fromEnv = Environment.GetEnvironmentVariable(UserDirEnv);
        var hasEnv = !string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv);
        var userDir = hasEnv ? fromEnv! : DefaultUserDir();

        var installed = hasEnv || Directory.Exists(userDir) || AddonConfPaths.Any(File.Exists);
        if (!installed) return null;

        return new Fcitx5RimeDistribution(
            userDir,
            SharedDataDirs.FirstOrDefault(Directory.Exists),
            ExternalTool.FindOnPath("rime_deployer"));
    }

    private static string DefaultUserDir()
    {
        var dataHome = Environment.GetEnvironmentVariable("XDG_DATA_HOME");
        if (string.IsNullOrWhiteSpace(dataHome))
            dataHome = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".local", "share");
        return Path.Combine(dataHome, "fcitx5", "rime");
    }
}
