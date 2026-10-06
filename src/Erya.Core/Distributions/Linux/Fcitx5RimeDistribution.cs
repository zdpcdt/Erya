namespace Erya.Core.Distributions.Linux;

/// <summary>fcitx5-rime — RIME addon for the fcitx5 input method framework.</summary>
public sealed class Fcitx5RimeDistribution : RimeDistributionBase
{
    /// <summary>librime's standalone deployer; the only scripted redeploy entry fcitx5 offers.</summary>
    private readonly string? _deployerPath;

    public Fcitx5RimeDistribution(string userDir, string? sharedDataDir, string? deployerPath)
    {
        UserDir = userDir;
        _deployerPath = deployerPath;
        SchemaSearchDirs = sharedDataDir is null ? [] : [sharedDataDir];
    }

    public override string Id => "fcitx5-rime";

    public override string DisplayName => "fcitx5 中州韵";

    public override string UserDir { get; }

    public override bool CanDeploy => _deployerPath is not null;

    public override async Task DeployAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDeploy)
            throw new InvalidOperationException("未找到 rime_deployer（librime 工具，通常在 librime-bin 包），无法触发重新部署。");

        // --build performs the same user-directory deployment the engine does;
        // the running fcitx5 picks the rebuilt artifacts up on next use.
        await ExternalTool.RunAsync(_deployerPath!, ["--build", UserDir], cancellationToken);
    }
}
