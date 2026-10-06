namespace Erya.Core.Distributions.MacOS;

/// <summary>
/// Detects 鼠鬚管 (Squirrel), the macOS RIME frontend. Squirrel installs into
/// "/Library/Input Methods" (all users) or "~/Library/Input Methods" (current
/// user only). Set <c>ERYA_SQUIRREL_APP</c> to point at a non-standard .app
/// bundle for development and CI.
/// </summary>
public sealed class SquirrelLocator : IRimeDistributionLocator
{
    public const string InstallDirEnv = "ERYA_SQUIRREL_APP";

    public IRimeDistribution? Locate()
    {
        var appDir = ResolveAppDir();
        return appDir is null ? null : new SquirrelDistribution(appDir);
    }

    private static string? ResolveAppDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable(InstallDirEnv);
        if (!string.IsNullOrWhiteSpace(fromEnv) && IsSquirrelApp(fromEnv))
            return fromEnv;

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return new[] { "/Library/Input Methods", Path.Combine(home, "Library", "Input Methods") }
            .Select(root => Path.Combine(root, "Squirrel.app"))
            .FirstOrDefault(IsSquirrelApp);
    }

    private static bool IsSquirrelApp(string dir)
        => File.Exists(Path.Combine(dir, "Contents", "MacOS", "Squirrel"));
}
