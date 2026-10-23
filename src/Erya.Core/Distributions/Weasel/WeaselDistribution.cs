using Erya.Core.Localization;
using System.Diagnostics;
using Erya.Core.Configuration;
using Erya.Core.Models;

namespace Erya.Core.Distributions.Weasel;

/// <summary>小狼毫 Weasel — the Windows RIME frontend.</summary>
public sealed class WeaselDistribution : RimeDistributionBase
{
    private string? DeployerPath => InstallDir is null ? null : Path.Combine(InstallDir, "WeaselDeployer.exe");

    public WeaselDistribution(string installDir)
    {
        InstallDir = installDir;
        var name = Path.GetFileName(installDir);
        var separator = name.IndexOf('-');
        Version = separator >= 0 && Version.TryParse(name[(separator + 1)..], out var version) ? version : null;

        // Shipped schemas (luna_pinyin & co.) live in <install>\data until the
        // user customizes them; the user directory shadows that copy.
        SchemaSearchDirs = [Path.Combine(installDir, "data")];
    }

    public override string Id => "weasel";

    public override string DisplayName => CoreLoc.Get("distName_weasel");

    public override Version? Version { get; }

    public override string? InstallDir { get; }

    public override string UserDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime");

    /// <summary>Weasel reads its panel looks and per-app behavior from weasel.yaml.</summary>
    public override string? UiConfigFile => "weasel.yaml";

    public override bool CanDeploy => DeployerPath is not null && File.Exists(DeployerPath);

    public override async Task DeployAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDeploy)
            throw new InvalidOperationException(CoreLoc.Format("weasel_deployerMissing", DeployerPath));

        // /deploy runs silently (a progress dialog only on failure) and exits
        // when done — including the WeaselServer maintenance cycle that makes
        // the running input method pick up the new build. Fire-and-forget here
        // would report success before anything actually happened.
        // A deployer started while another one holds WeaselDeployerExclusiveMutex
        // exits with code 1 immediately — retry once for that contention.
        for (int attempt = 1; ; attempt++)
        {
            using var process = Process.Start(new ProcessStartInfo(DeployerPath!)
            {
                UseShellExecute = false,
                Arguments = "/deploy",
            }) ?? throw new InvalidOperationException(CoreLoc.Get("weasel_deployerStartFailed"));
            await process.WaitForExitAsync(cancellationToken);
            if (process.ExitCode == 0) return;
            if (attempt >= 2)
                throw new InvalidOperationException(
                    CoreLoc.Format("weasel_deployFailed", process.ExitCode));
            await Task.Delay(1500, cancellationToken);
        }
    }
}
