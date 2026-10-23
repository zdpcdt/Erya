using Erya.Core.Localization;

namespace Erya.Core.Distributions.Linux;

/// <summary>fcitx5-rime — RIME addon for the fcitx5 input method framework.</summary>
public sealed class Fcitx5RimeDistribution : RimeDistributionBase
{
    /// <summary>librime's standalone deployer; the only scripted redeploy entry fcitx5 offers.</summary>
    private readonly string? _deployerPath;
    private readonly string? _sharedDataDir;

    public Fcitx5RimeDistribution(string userDir, string? sharedDataDir, string? deployerPath,
        Version? packageVersion = null)
    {
        UserDir = userDir;
        _deployerPath = deployerPath;
        _sharedDataDir = sharedDataDir;
        Version = packageVersion;
        SchemaSearchDirs = sharedDataDir is null ? [] : [sharedDataDir];
    }

    public override string Id => "fcitx5-rime";

    public override string DisplayName => CoreLoc.Get("distName_fcitx5");

    public override Version? Version { get; }

    /// <summary>The shared data dir doubles as the install location display —
    /// that is where the distribution's schemas and base config live.</summary>
    public override string? InstallDir => _sharedDataDir;

    public override string UserDir { get; }

    public override bool CanDeploy => _deployerPath is not null;

    /// <summary>
    /// The argv handed to rime_deployer. The shared data dir is not optional
    /// in practice: librime (verified on 1.16) defaults it to the working
    /// directory when the second --build argument is omitted, so every schema
    /// is reported missing and the build aborts without writing anything.
    /// </summary>
    public IReadOnlyList<string> DeployerArguments =>
        _sharedDataDir is { } shared ? ["--build", UserDir, shared] : ["--build", UserDir];

    public override async Task DeployAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDeploy)
            throw new InvalidOperationException(CoreLoc.Get("fcitx_deployerMissing"));

        // --build performs the same user-directory deployment the engine does;
        // the running fcitx5 picks the rebuilt artifacts up on next use.
        await ExternalTool.RunAsync(_deployerPath!, DeployerArguments, cancellationToken);
    }
}
