using System.Text.RegularExpressions;

namespace Erya.Core.Distributions.Linux;

/// <summary>
/// Best-effort version probe for Linux frontend packages (fcitx5-rime,
/// ibus-rime) via whichever package manager is installed — Debian's
/// <c>dpkg-query</c>, Red Hat's <c>rpm</c>, or Arch's <c>pacman</c>. A missing
/// manager or package simply yields null; results are memoized per package
/// name since locators re-run distribution detection.
/// </summary>
public static class LinuxPackageProbe
{
    /// <summary>
    /// First dotted number in the raw output. Skips Debian epochs
    /// (<c>1:0.9.0-1</c> → 0.9.0), package-name digits (<c>fcitx5-rime 5.1.5</c>
    /// → 5.1.5) and distro revisions (<c>5.1.5-1build2</c> → 5.1.5) without
    /// caring which manager produced the string.
    /// </summary>
    private static readonly Regex DottedVersion = new(@"\d+(?:\.\d+)+", RegexOptions.Compiled);

    private static readonly Dictionary<string, Version?> Cache = [];
    private static readonly object CacheGate = new();

    public static Version? Query(string packageName)
    {
        lock (CacheGate)
        {
            if (Cache.TryGetValue(packageName, out var cached)) return cached;
        }

        var version = QueryUncached(packageName);
        lock (CacheGate)
        {
            Cache[packageName] = version;
        }
        return version;
    }

    private static Version? QueryUncached(string packageName)
    {
        // Manager availability is the distro signal — no os-release needed.
        // Debian's version string is the noisiest, so try it first only when
        // present; each query is "exit 0 + parseable version or nothing".
        (string Tool, string[] Arguments)[] candidates =
        [
            ("dpkg-query", ["-W", "-f=${Version}", packageName]),
            ("rpm", ["-q", "--qf", "%{VERSION}", packageName]),
            ("pacman", ["-Q", packageName]),
        ];

        foreach (var (tool, arguments) in candidates)
        {
            var path = ExternalTool.FindOnPath(tool);
            if (path is null) continue;

            try
            {
                var (exitCode, output) = ExternalTool.RunCapture(path, arguments);
                if (exitCode != 0 || output.Length == 0) continue;
                if (TryParseVersion(output) is { } version) return version;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                // Tool exists but failed to run (permissions, corrupted install):
                // fall through to the next candidate.
            }
        }
        return null;
    }

    /// <summary>Extracts a display version from raw package-manager output.</summary>
    public static Version? TryParseVersion(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var match = DottedVersion.Match(raw);
        return match.Success && Version.TryParse(match.Value, out var version) ? version : null;
    }
}
