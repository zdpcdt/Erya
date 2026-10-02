using System.Collections.Frozen;

namespace Erya.Core.Models;

/// <summary>
/// The schema catalog as seen by the engine right now: every schema that can
/// be enabled (shipped files found on disk), plus the ids currently active
/// (i.e. listed in the effective schema_list).
/// </summary>
public sealed record RimeSchemaSnapshot(
    IReadOnlyList<RimeSchema> Schemas,
    FrozenSet<string> SelectedSchemaIds);
