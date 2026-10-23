using Erya.Core.Localization;

namespace Erya.Core.Distributions.Linux;

/// <summary>ibus-rime — RIME engine for the ibus input method framework.</summary>
public sealed class IbusRimeDistribution : RimeDistributionBase
{
    private readonly string? _ibusPath;
    private readonly string? _deployerPath;
    private readonly string? _sharedDataDir;

    public IbusRimeDistribution(string userDir, string? sharedDataDir, string? ibusPath, string? deployerPath,
        Version? packageVersion = null)
    {
        UserDir = userDir;
        _ibusPath = ibusPath;
        _deployerPath = deployerPath;
        _sharedDataDir = sharedDataDir;
        Version = packageVersion;
        SchemaSearchDirs = sharedDataDir is null ? [] : [sharedDataDir];
    }

    public override string Id => "ibus-rime";

    public override string DisplayName => CoreLoc.Get("distName_ibus");

    public override Version? Version { get; }

    /// <summary>The shared data dir doubles as the install location display —
    /// that is where the distribution's schemas and base config live.</summary>
    public override string? InstallDir => _sharedDataDir;

    public override string UserDir { get; }

    public override bool CanDeploy => _ibusPath is not null || _deployerPath is not null;

    /// <summary>
    /// The argv handed to rime_deployer on the fallback path (ibus not on
    /// PATH). As with fcitx5-rime, the shared data dir must be passed
    /// explicitly — omitted, librime defaults it to the working directory and
    /// the build fails with "missing input schema" for every schema.
    /// </summary>
    public IReadOnlyList<string> DeployerArguments =>
        _sharedDataDir is { } shared ? ["--build", UserDir, shared] : ["--build", UserDir];

    public override async Task DeployAsync(CancellationToken cancellationToken = default)
    {
        // The documented redeploy for ibus-rime: restart the daemon, the RIME
        // engine deploys as it comes back up. rime_deployer is the fallback
        // for environments where ibus itself is not on PATH.
        if (_ibusPath is not null)
        {
            await ExternalTool.RunAsync(_ibusPath, ["restart"], cancellationToken);
            return;
        }

        if (_deployerPath is not null)
        {
            await ExternalTool.RunAsync(_deployerPath, DeployerArguments, cancellationToken);
            return;
        }

        throw new InvalidOperationException(
            CoreLoc.Get("ibus_deployerMissing"));
    }
}
