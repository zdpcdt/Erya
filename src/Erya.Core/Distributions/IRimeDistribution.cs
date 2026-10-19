using Erya.Core.Configuration;
using Erya.Core.Models;

namespace Erya.Core.Distributions;
/// <summary>
/// A RIME distribution (frontend) detected on the current machine —
/// Weasel (Windows), Squirrel (macOS), fcitx5-rime / ibus-rime (Linux).
/// <para>
/// A distribution owns two things Erya cares about: where the RIME user
/// directory lives (YAML schemas, user dictionary) and how to trigger a
/// redeploy so configuration changes take effect.
/// </para>
/// </summary>
public interface IRimeDistribution
{
    /// <summary>Stable identifier, e.g. "weasel".</summary>
    string Id { get; }

    /// <summary>Localized display name, e.g. "小狼毫 Weasel".</summary>
    string DisplayName { get; }

    /// <summary>Version of the installed frontend, if detectable.</summary>
    Version? Version { get; }

    /// <summary>Installation directory, or null when not detectable.</summary>
    string? InstallDir { get; }

    /// <summary>
    /// RIME user configuration directory holding schema/config YAML and
    /// the user dictionary (e.g. %APPDATA%\Rime on Windows).
    /// </summary>
    string UserDir { get; }

    /// <summary>
    /// The frontend's own base config file whose patch drives candidate
    /// panel appearance and per-app behavior — <c>squirrel.yaml</c> on macOS,
    /// <c>weasel.yaml</c> on Windows. Null on Linux, where fcitx5/ibus draw
    /// the candidate panel themselves and RIME YAML has no looks to configure.
    /// </summary>
    string? UiConfigFile { get; }

    /// <summary>Whether a deployer entry point is available on this machine.</summary>
    bool CanDeploy { get; }

    /// <summary>
    /// Access to this distribution's YAML config family (base lookup + patch
    /// writes). Always available, even when <see cref="CanDeploy"/> is false.
    /// </summary>
    RimeConfigStore GetConfigStore();

    /// <summary>
    /// Triggers a redeploy so edited YAML configuration takes effect.
    /// Throws <see cref="InvalidOperationException"/> when <see cref="CanDeploy"/> is false.
    /// </summary>
    Task DeployAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists schemas visible to the engine: every <c>*.schema.yaml</c> found
    /// on disk, plus which ids are currently active (effective
    /// <c>schema_list</c>: <c>default.custom.yaml</c> patch over <c>default.yaml</c>).
    /// </summary>
    Task<RimeSchemaSnapshot> GetSchemasAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists the selected schema list by writing a
    /// <c>default.custom.yaml</c> patch. Original <c>default.yaml</c> files
    /// shipped by schemas are never modified — that is the RIME upgrade rule.
    /// </summary>
    Task SetSelectedSchemasAsync(IReadOnlyList<string> schemaIds, CancellationToken cancellationToken = default);
}
