using Erya.Core.Configuration;
using Erya.Core.Models;

namespace Erya.Core.Distributions;

/// <summary>
/// Shared plumbing for concrete distributions: schema discovery against the
/// user directory plus optional shared-data directories. Subclasses decide
/// where those directories are and how a redeploy is triggered.
/// </summary>
public abstract class RimeDistributionBase : IRimeDistribution
{
    /// <summary>
    /// Extra directories scanned for <c>*.schema.yaml</c> after the user dir
    /// (distribution-bundled schemas). Missing directories are skipped by the
    /// catalog, so candidates may be listed unconditionally. The same
    /// directories also hold shared base config (<c>default.yaml</c> & co.)
    /// resolved by <see cref="GetConfigStore"/>.
    /// </summary>
    protected string[] SchemaSearchDirs { get; init; } = [];

    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public virtual Version? Version => null;

    public virtual string? InstallDir => null;

    public abstract string UserDir { get; }

    /// <summary>Overridden by frontends that read an appearance YAML (squirrel/weasel).</summary>
    public virtual string? UiConfigFile => null;

    public RimeConfigStore GetConfigStore() => new(UserDir, SchemaSearchDirs);

    public abstract bool CanDeploy { get; }

    public abstract Task DeployAsync(CancellationToken cancellationToken = default);

    public Task<RimeSchemaSnapshot> GetSchemasAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new RimeSchemaCatalog(UserDir, SchemaSearchDirs).LoadSnapshot());

    public Task SetSelectedSchemasAsync(IReadOnlyList<string> schemaIds, CancellationToken cancellationToken = default)
    {
        new RimeSchemaCatalog(UserDir).WriteSelectedSchemas(schemaIds);
        return Task.CompletedTask;
    }

    public Task CreateSchemaStubAsync(string schemaId, string name, CancellationToken cancellationToken = default)
        => Task.FromResult(new RimeSchemaCatalog(UserDir).CreateSchemaStub(schemaId, name));
}
