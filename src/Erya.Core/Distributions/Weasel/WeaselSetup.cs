using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using Erya.Core.Localization;

namespace Erya.Core.Distributions.Weasel;

/// <summary>The latest Weasel release: tag plus the installer asset's URL/size.</summary>
public sealed record WeaselReleaseInfo(string Tag, string InstallerUrl, long InstallerSizeBytes);

public interface IWeaselSetup
{
    /// <summary>Resolves the newest Weasel release and its <c>*-installer.exe</c> asset.</summary>
    Task<WeaselReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads the installer to a temp file, reporting 0–100 progress.</summary>
    /// <returns>Path of the downloaded installer on this machine.</returns>
    Task<string> DownloadInstallerAsync(
        WeaselReleaseInfo release, IProgress<double>? percent = null, CancellationToken cancellationToken = default);
}

/// <summary>
/// One-click Weasel setup for machines without any RIME distribution: ask the
/// GitHub API for the newest release, pick the <c>*-installer.exe</c> asset
/// (one per release — debug symbols ship alongside), stream it to %TEMP% with
/// progress. Launching the installer and elevation stay with the OS shell;
/// this class only fetches bytes.
/// </summary>
public sealed class WeaselSetup : IWeaselSetup
{
    private const string LatestReleaseApiUrl = "https://api.github.com/repos/rime/weasel/releases/latest";

    /// <summary>Sanity cap — the installer is ~12 MB today.</summary>
    private const long MaxInstallerBytes = 512L * 1024 * 1024;

    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    private readonly HttpClient _client;
    private readonly string? _downloadDir;

    public WeaselSetup(HttpMessageHandler? handler = null, string? downloadDir = null)
    {
        _client = handler is null ? new HttpClient { Timeout = Timeout } : new HttpClient(handler) { Timeout = Timeout };
        // The GitHub API rejects requests without a User-Agent with 403.
        _client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "Erya");
        _client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        _downloadDir = downloadDir;
    }

    public async Task<WeaselReleaseInfo> GetLatestReleaseAsync(CancellationToken cancellationToken = default)
    {
        HttpResponseMessage response;
        try
        {
            response = await _client.GetAsync(LatestReleaseApiUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(CoreLoc.Format("setup_queryFailed", ex.Message), ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(CoreLoc.Format("setup_queryTimeout"), ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(CoreLoc.Format("setup_httpError", (int)response.StatusCode));

            GitHubRelease? release;
            try
            {
                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                release = await JsonSerializer.DeserializeAsync(stream, WeaselReleaseContext.Default.GitHubRelease, cancellationToken);
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(CoreLoc.Format("setup_badResponse"), ex);
            }

            var tag = release?.Tag ?? "";
            var installer = release?.Assets?.FirstOrDefault(a => a.Name?.EndsWith("-installer.exe", StringComparison.OrdinalIgnoreCase) == true);
            if (installer?.BrowserDownloadUrl is not { Length: > 0 } url)
                throw new InvalidOperationException(CoreLoc.Format("setup_noInstallerAsset", tag));

            return new WeaselReleaseInfo(tag, url, installer.Size);
        }
    }

    public async Task<string> DownloadInstallerAsync(
        WeaselReleaseInfo release, IProgress<double>? percent = null, CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(InstallerDir());
        var targetPath = Path.Combine(InstallerDir(), $"{SanitizeTag(release.Tag)}-installer.exe");

        HttpResponseMessage response;
        try
        {
            response = await _client.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        }
        catch (HttpRequestException ex)
        {
            throw new InvalidOperationException(CoreLoc.Format("setup_downloadFailed", ex.Message), ex);
        }
        catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException(CoreLoc.Format("setup_downloadTimeout"), ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new InvalidOperationException(CoreLoc.Format("setup_downloadHttpError", (int)response.StatusCode));

            // Trust the API-reported size for progress; the header may be absent
            // when GitHub serves the asset via redirect.
            var total = release.InstallerSizeBytes > 0 ? release.InstallerSizeBytes : response.Content.Headers.ContentLength;
            await using var network = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var output = File.Create(targetPath);
            var buffer = new byte[64 * 1024];
            long downloaded = 0;
            int read;
            while ((read = await network.ReadAsync(buffer, cancellationToken)) > 0)
            {
                downloaded += read;
                if (downloaded > MaxInstallerBytes)
                    throw new InvalidOperationException(CoreLoc.Get("setup_tooLarge"));
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                if (total is > 0)
                    percent?.Report(Math.Min(100, downloaded * 100.0 / total.Value));
            }
        }
        return targetPath;
    }

    private string InstallerDir()
        => _downloadDir ?? Path.Combine(Path.GetTempPath(), "erya-weasel");

    /// <summary>Tags look like "0.17.4"; strip anything that could not be a file name.</summary>
    private static string SanitizeTag(string tag)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string([.. tag.Where(c => !invalid.Contains(c))]).Trim();
    }
}

internal sealed record GitHubAsset(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("size")] long Size,
    [property: JsonPropertyName("browser_download_url")] string? BrowserDownloadUrl);

internal sealed record GitHubRelease(
    [property: JsonPropertyName("tag_name")] string? Tag,
    [property: JsonPropertyName("assets")] List<GitHubAsset>? Assets);

[JsonSerializable(typeof(GitHubRelease))]
internal sealed partial class WeaselReleaseContext : JsonSerializerContext;
