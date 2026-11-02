using System.Net;
using System.Text;
using Erya.Core.Distributions.Weasel;

namespace Erya.Core.Tests;

/// <summary>
/// One-click Weasel setup: the GitHub API is faked with a routing handler
/// serving release JSON / installer bytes, so asset picking, download and
/// progress run for real while the tests stay hermetic.
/// </summary>
public sealed class WeaselSetupTests : IDisposable
{
    private readonly string _root;

    public WeaselSetupTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "erya-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    private WeaselSetup Create(HttpMessageHandler handler)
        => new(handler, Path.Combine(_root, "download"));

    private static string ReleaseJson(params (string Name, long Size, string Url)[] assets)
    {
        var sb = new StringBuilder("{\"tag_name\":\"0.17.4\",\"assets\":[");
        for (var i = 0; i < assets.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append($"{{\"name\":\"{assets[i].Name}\",\"size\":{assets[i].Size},\"browser_download_url\":\"{assets[i].Url}\"}}");
        }
        return sb.Append("]}").ToString();
    }

    /// <summary>Serves release JSON for the API call, installer bytes otherwise.</summary>
    private sealed class RoutingHandler(Func<string, HttpResponseMessage> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(route(request.RequestUri?.AbsoluteUri ?? ""));
    }

    private static HttpResponseMessage Json(string body) => new(HttpStatusCode.OK)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    [Fact]
    public async Task Latest_release_resolves_the_installer_asset_only()
    {
        var setup = Create(new RoutingHandler(url => Json(ReleaseJson(
            ("debug_symbols.7z", 32_000_000, "https://example.com/debug.7z"),
            ("weasel-0.17.4.0-installer.exe", 12_400_000, "https://example.com/weasel-0.17.4.0-installer.exe")))));

        var release = await setup.GetLatestReleaseAsync();

        Assert.Equal("0.17.4", release.Tag);
        Assert.EndsWith("weasel-0.17.4.0-installer.exe", release.InstallerUrl);
        Assert.Equal(12_400_000, release.InstallerSizeBytes);
    }

    [Fact]
    public async Task Release_without_installer_asset_fails_with_the_tag()
    {
        var setup = Create(new RoutingHandler(_ => Json(ReleaseJson(
            ("debug_symbols.7z", 1, "https://example.com/debug.7z")))));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => setup.GetLatestReleaseAsync());

        Assert.Contains("0.17.4", error.Message);
        Assert.Contains("installer", error.Message);
    }

    [Fact]
    public async Task Api_failures_surface_as_friendly_errors()
    {
        var setup = Create(new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.Forbidden)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => setup.GetLatestReleaseAsync());

        Assert.Contains("HTTP 403", error.Message);
    }

    [Fact]
    public async Task Installer_downloads_to_disk_with_progress()
    {
        var payload = "MZ fake installer bytes"u8.ToArray();
        var release = new WeaselReleaseInfo("0.17.4", "https://example.com/weasel.exe", payload.Length);
        var setup = Create(new RoutingHandler(url =>
            url.Contains("api.github.com") ? Json(ReleaseJson(("weasel.exe", payload.Length, "https://example.com/weasel.exe")))
            : new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(payload) }));

        var percents = new List<double>();
        var path = await setup.DownloadInstallerAsync(release, new Progress<double>(percents.Add));
        await Task.Delay(50); // Progress<T> posts asynchronously

        Assert.Equal(payload, await File.ReadAllBytesAsync(path));
        Assert.StartsWith(Path.Combine(_root, "download"), path);
        Assert.EndsWith("0.17.4-installer.exe", path);
        Assert.Contains(100, percents);
    }

    [Fact]
    public async Task Download_failures_surface_as_friendly_errors()
    {
        var release = new WeaselReleaseInfo("0.17.4", "https://example.com/weasel.exe", 1);
        var setup = Create(new RoutingHandler(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(
            () => setup.DownloadInstallerAsync(release));

        Assert.Contains("HTTP 500", error.Message);
    }
}
