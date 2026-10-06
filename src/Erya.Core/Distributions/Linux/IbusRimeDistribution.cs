namespace Erya.Core.Distributions.Linux;

/// <summary>ibus-rime — RIME engine for the ibus input method framework.</summary>
public sealed class IbusRimeDistribution : RimeDistributionBase
{
    private readonly string? _ibusPath;
    private readonly string? _deployerPath;

    public IbusRimeDistribution(string userDir, string? sharedDataDir, string? ibusPath, string? deployerPath)
    {
        UserDir = userDir;
        _ibusPath = ibusPath;
        _deployerPath = deployerPath;
        SchemaSearchDirs = sharedDataDir is null ? [] : [sharedDataDir];
    }

    public override string Id => "ibus-rime";

    public override string DisplayName => "ibus 中州韵";

    public override string UserDir { get; }

    public override bool CanDeploy => _ibusPath is not null || _deployerPath is not null;

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
            await ExternalTool.RunAsync(_deployerPath, ["--build", UserDir], cancellationToken);
            return;
        }

        throw new InvalidOperationException(
            "未找到 ibus 或 rime_deployer，无法触发重新部署。请安装 ibus 或 librime-bin。");
    }
}
