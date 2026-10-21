using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Erya.Core.Distributions.MacOS;

/// <summary>鼠鬚管 Squirrel — the macOS RIME frontend.</summary>
public sealed class SquirrelDistribution : RimeDistributionBase
{
    /// <summary>How long to wait for the running Squirrel instance to finish a
    /// redeploy before reporting failure; generous enough for big schema sets.</summary>
    private static readonly TimeSpan RedeployTimeout = TimeSpan.FromSeconds(20);

    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(250);

    private readonly string _binaryPath;
    private readonly string _userDir;

    public SquirrelDistribution(string appDir)
        : this(appDir, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Rime"))
    {
    }

    /// <summary>Test seam: point UserDir at a sandbox directory.</summary>
    internal SquirrelDistribution(string appDir, string userDir)
    {
        InstallDir = appDir;
        _binaryPath = Path.Combine(appDir, "Contents", "MacOS", "Squirrel");
        _userDir = userDir;
        Version = ParseVersion(Path.Combine(appDir, "Contents", "Info.plist"));

        // Bundled schemas ship inside the app bundle; missing dirs are skipped
        // by the schema catalog, so existence doesn't need checking here.
        SchemaSearchDirs = [Path.Combine(appDir, "Contents", "SharedSupport")];
    }

    public override string Id => "squirrel";

    public override string DisplayName => "鼠鬚管 Squirrel";

    public override string? InstallDir { get; }

    public override string UserDir => _userDir;

    public override Version? Version { get; }

    /// <summary>Squirrel reads its panel looks and per-app behavior from squirrel.yaml.</summary>
    public override string? UiConfigFile => "squirrel.yaml";

    public override bool CanDeploy => File.Exists(_binaryPath);

    /// <summary>
    /// Squirrel --reload only posts the SquirrelReloadNotification distributed
    /// notification and exits; the redeploy itself runs inside the resident
    /// Squirrel instance afterwards, silently. To match the other frontends —
    /// where the deploy tool runs to completion under this await — capture a
    /// build stamp first and wait for it to advance, so callers can report the
    /// real outcome. Without a resident instance there is no receiver for the
    /// notification and nothing would ever advance the stamp; skip the wait.
    /// </summary>
    public override async Task DeployAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDeploy)
            throw new InvalidOperationException($"未找到鼠鬚管主程序：{_binaryPath}（鼠鬚管是否已安装？）");

        var instanceRunning = IsSquirrelRunning();
        var stamp = BuildStamp();
        await ExternalTool.RunAsync(_binaryPath, ["--reload"], cancellationToken);

        if (instanceRunning && !await WaitForBuildAdvanceAsync(stamp, RedeployTimeout, cancellationToken))
            throw new InvalidOperationException(
                $"已通知鼠鬚管重新部署，但 {(int)RedeployTimeout.TotalSeconds} 秒内未检测到部署完成。" +
                "可从输入法菜单手动「重新部署」，或稍后重试。");
    }

    internal static bool IsSquirrelRunning()
    {
        try
        {
            return Process.GetProcessesByName("Squirrel").Length > 0;
        }
        catch (InvalidOperationException)
        {
            // Process enumeration unavailable — assume the instance is there so
            // the verification wait still happens rather than a hollow success.
            return true;
        }
    }

    /// <summary>Newest write time across the user build directory; MinValue when absent.</summary>
    internal DateTimeOffset BuildStamp()
    {
        try
        {
            var buildDir = Path.Combine(UserDir, "build");
            if (!Directory.Exists(buildDir)) return DateTimeOffset.MinValue;
            var max = new DateTimeOffset(Directory.GetLastWriteTimeUtc(buildDir), TimeSpan.Zero);
            foreach (var file in Directory.EnumerateFiles(buildDir, "*", SearchOption.AllDirectories))
            {
                var written = new DateTimeOffset(File.GetLastWriteTimeUtc(file), TimeSpan.Zero);
                if (written > max) max = written;
            }
            return max;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return DateTimeOffset.MinValue;
        }
    }

    internal async Task<bool> WaitForBuildAdvanceAsync(
        DateTimeOffset before, TimeSpan timeout, CancellationToken cancellationToken)
    {
        var deadline = DateTimeOffset.UtcNow + timeout;
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(PollInterval, cancellationToken);
            if (BuildStamp() > before) return true;
        }
        return BuildStamp() > before;
    }

    private static Version? ParseVersion(string infoPlistPath)
    {
        try
        {
            var text = File.ReadAllText(infoPlistPath);
            var match = Regex.Match(
                text,
                "<key>CFBundleShortVersionString</key>\\s*<string>([^<]+)</string>");
            return match.Success && Version.TryParse(match.Groups[1].Value, out var version)
                ? version
                : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
