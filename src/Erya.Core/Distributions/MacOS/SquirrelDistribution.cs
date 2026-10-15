using System.Text.RegularExpressions;

namespace Erya.Core.Distributions.MacOS;

/// <summary>鼠鬚管 Squirrel — the macOS RIME frontend.</summary>
public sealed class SquirrelDistribution : RimeDistributionBase
{
    private readonly string _binaryPath;

    public SquirrelDistribution(string appDir)
    {
        InstallDir = appDir;
        _binaryPath = Path.Combine(appDir, "Contents", "MacOS", "Squirrel");
        UserDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Rime");
        Version = ParseVersion(Path.Combine(appDir, "Contents", "Info.plist"));

        // Bundled schemas ship inside the app bundle; missing dirs are skipped
        // by the schema catalog, so existence doesn't need checking here.
        SchemaSearchDirs = [Path.Combine(appDir, "Contents", "SharedSupport")];
    }

    public override string Id => "squirrel";

    public override string DisplayName => "鼠鬚管 Squirrel";

    public override string? InstallDir { get; }

    public override string UserDir { get; }

    public override Version? Version { get; }

    /// <summary>Squirrel reads its panel looks and per-app behavior from squirrel.yaml.</summary>
    public override string? UiConfigFile => "squirrel.yaml";

    public override bool CanDeploy => File.Exists(_binaryPath);

    public override Task DeployAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDeploy)
            throw new InvalidOperationException($"未找到鼠鬚管主程序：{_binaryPath}（鼠鬚管是否已安装？）");

        return ExternalTool.RunAsync(_binaryPath, ["--reload"], cancellationToken);
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
