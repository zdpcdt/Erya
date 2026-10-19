namespace Erya.Core.Packages;

/// <summary>
/// One installable community configuration package — a GitHub repository
/// snapshot (codeload tarball) whose files are copied into the RIME user
/// directory. This is the GUI counterpart of 东风破 plum's recipes: instead of
/// a recipe DSL, each entry carries a plain exclude list tuned per repository
/// (docs/, CI configs, build artifacts and other non-RIME content stay out).
/// </summary>
/// <param name="MarkerSchemas">
/// Schema files unique to this package — the "already installed" probe. A
/// package counts as installed when any marker file exists in the user
/// directory. Names must be package-distinctive (forks rename their schemas:
/// 白霜 is a fork of 雾凇 but ships <c>rime_frost.schema.yaml</c>).
/// </param>
public sealed record SchemaPackage(
    string Id,
    string Name,
    string Description,
    string Homepage,
    string Owner,
    string Repo,
    string Branch,
    string[] Exclude,
    string[] MarkerSchemas)
{
    /// <summary>Codeload URL — no git required, one plain HTTPS GET.</summary>
    public string ArchiveUrl => $"https://codeload.github.com/{Owner}/{Repo}/tar.gz/refs/heads/{Branch}";

    /// <summary>True when any marker schema is present in the user directory.</summary>
    public bool IsInstalledIn(string userDir)
        => MarkerSchemas.Any(marker => File.Exists(Path.Combine(userDir, marker)));

    /// <summary>
    /// Excluded from every package: VCS/CI metadata, docs and license blobs
    /// that ride along in every repository.
    /// </summary>
    public static readonly string[] DefaultExclude =
        [".git*", "*.md", "LICENSE*", "NOTICE*", ".nojekyll*", "CMakeLists.txt"];

    public bool IsExcluded(string relativePath)
        => DefaultExclude.Concat(Exclude).Any(pattern => Matches(relativePath, pattern));

    /// <summary>
    /// Wildcard match where <c>*</c> crosses directory boundaries, so
    /// <c>".git*"</c> covers <c>.github/workflows/ci.yml</c> and <c>"others*"</c>
    /// covers both <c>others/essay.txt</c> and <c>others.txt</c>.
    /// </summary>
    public static bool Matches(string path, string pattern)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        ArgumentException.ThrowIfNullOrEmpty(pattern);

        var parts = pattern.Split('*');
        if (parts.Length == 1) return path == pattern;
        if (!path.StartsWith(parts[0], StringComparison.Ordinal)) return false;

        var position = parts[0].Length;
        for (var i = 1; i < parts.Length - 1; i++)
        {
            var next = path.IndexOf(parts[i], position, StringComparison.Ordinal);
            if (next < 0) return false;
            position = next + parts[i].Length;
        }

        var last = parts[^1];
        return last.Length == 0 || path.EndsWith(last, StringComparison.Ordinal) && path.Length - position >= last.Length;
    }
}
