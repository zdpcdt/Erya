using System.Diagnostics;
using Erya.Core.Configuration;
using Erya.Core.Models;

namespace Erya.Core.Distributions.Weasel;

/// <summary>小狼毫 Weasel — the Windows RIME frontend.</summary>
public sealed class WeaselDistribution : IRimeDistribution
{
    private string? DeployerPath => InstallDir is null ? null : Path.Combine(InstallDir, "WeaselDeployer.exe");

    public WeaselDistribution(string installDir)
    {
        InstallDir = installDir;
        var name = Path.GetFileName(installDir);
        var separator = name.IndexOf('-');
        Version = separator >= 0 && Version.TryParse(name[(separator + 1)..], out var version) ? version : null;
    }

    public string Id => "weasel";

    public string DisplayName => "小狼毫 Weasel";

    public Version? Version { get; }

    public string? InstallDir { get; }

    public string UserDir => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Rime");

    public bool CanDeploy => DeployerPath is not null && File.Exists(DeployerPath);

    public Task DeployAsync(CancellationToken cancellationToken = default)
    {
        if (!CanDeploy)
            throw new InvalidOperationException($"未找到部署器：{DeployerPath}（小狼毫是否已安装？）");

        Process.Start(new ProcessStartInfo(DeployerPath!)
        {
            UseShellExecute = true,
            Arguments = "/deploy",
        });
        return Task.CompletedTask;
    }

    public Task<RimeSchemaSnapshot> GetSchemasAsync(CancellationToken cancellationToken = default)
    {
        // Shipped schemas (luna_pinyin & co.) live in <install>\data until the
        // user customizes them; the user directory shadows that copy.
        var catalog = InstallDir is { } install
            ? new RimeSchemaCatalog(UserDir, [Path.Combine(install, "data")])
            : new RimeSchemaCatalog(UserDir);
        return Task.FromResult(catalog.LoadSnapshot());
    }

    public Task SetSelectedSchemasAsync(IReadOnlyList<string> schemaIds, CancellationToken cancellationToken = default)
    {
        var catalog = new RimeSchemaCatalog(UserDir);
        catalog.WriteSelectedSchemas(schemaIds);
        return Task.CompletedTask;
    }
}
