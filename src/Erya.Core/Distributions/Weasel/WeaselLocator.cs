namespace Erya.Core.Distributions.Weasel;

/// <summary>
/// Detects 小狼毫 (Weasel), the Windows RIME frontend.
/// <para>
/// Weasel installs into "Program Files\Rime\weasel-&lt;version&gt;" (32-bit
/// Program Files on older releases). Set <c>ERYA_WEASEL_DIR</c> to point at a
/// non-standard install for development and CI.
/// </para>
/// </summary>
public sealed class WeaselLocator : IRimeDistributionLocator
{
    public const string InstallDirEnv = "ERYA_WEASEL_DIR";

    public IRimeDistribution? Locate()
    {
        var installDir = ResolveInstallDir();
        return installDir is null ? null : new WeaselDistribution(installDir);
    }

    private static string? ResolveInstallDir()
    {
        var fromEnv = Environment.GetEnvironmentVariable(InstallDirEnv);
        if (!string.IsNullOrWhiteSpace(fromEnv) && Directory.Exists(fromEnv))
            return fromEnv;

        var roots = new[]
            {
                Environment.SpecialFolder.ProgramFilesX86,
                Environment.SpecialFolder.ProgramFiles,
            }
            .Select(Environment.GetFolderPath)
            .Select(root => Path.Combine(root, "Rime"))
            .Where(Directory.Exists)
            .SelectMany(Directory.EnumerateDirectories);

        return roots
            .Where(dir => File.Exists(Path.Combine(dir, "WeaselDeployer.exe")))
            .OrderByDescending(ParseVersionFromDirName)
            .FirstOrDefault();
    }

    private static Version? ParseVersionFromDirName(string dir)
    {
        var name = Path.GetFileName(dir);
        var separator = name.IndexOf('-');
        if (separator < 0) return null;
        return Version.TryParse(name[(separator + 1)..], out var version) ? version : null;
    }
}
