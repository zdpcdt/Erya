using Erya.Core.Distributions.MacOS;

namespace Erya.Core.Tests;

/// <summary>
/// DeployAsync waits for the resident Squirrel instance to actually redeploy
/// (Squirrel --reload only posts a distributed notification). These tests
/// exercise the wait against a sandboxed user dir — the binary is never run.
/// </summary>
public sealed class SquirrelDeployWaitTests : IDisposable
{
    private readonly string _root;

    public SquirrelDeployWaitTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "erya-tests", Guid.NewGuid().ToString("N"));
        var appDir = Path.Combine(_root, "Squirrel.app");
        Directory.CreateDirectory(Path.Combine(appDir, "Contents", "MacOS"));
        File.WriteAllText(Path.Combine(appDir, "Contents", "MacOS", "Squirrel"), "binary");
        Directory.CreateDirectory(UserDir);
    }

    private string UserDir => Path.Combine(_root, "Library", "Rime");

    private SquirrelDistribution NewDistribution() => new(
        Path.Combine(_root, "Squirrel.app"), UserDir);

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* best effort */ }
    }

    [Fact]
    public async Task BuildStamp_is_min_value_without_build_dir_and_advances_on_write()
    {
        var distribution = NewDistribution();

        Assert.Equal(DateTimeOffset.MinValue, distribution.BuildStamp());

        Directory.CreateDirectory(Path.Combine(UserDir, "build"));
        File.WriteAllText(Path.Combine(UserDir, "build", "default.yaml"), "schema_list:");

        Assert.True(distribution.BuildStamp() > DateTimeOffset.MinValue);
    }

    [Fact]
    public async Task Wait_returns_true_when_build_dir_appears_within_timeout()
    {
        var distribution = NewDistribution();
        var wait = distribution.WaitForBuildAdvanceAsync(
            distribution.BuildStamp(), TimeSpan.FromSeconds(5), CancellationToken.None);

        await Task.Delay(300);
        Directory.CreateDirectory(Path.Combine(UserDir, "build"));
        File.WriteAllText(Path.Combine(UserDir, "build", "default.yaml"), "schema_list:");

        Assert.True(await wait);
    }

    [Fact]
    public async Task Wait_times_out_when_nothing_advances()
    {
        var distribution = NewDistribution();

        // No build dir ever appears: nothing can advance the stamp.
        var advanced = await distribution.WaitForBuildAdvanceAsync(
            DateTimeOffset.MinValue, TimeSpan.FromMilliseconds(400), CancellationToken.None);

        Assert.False(advanced);
    }

    [Fact]
    public async Task Wait_passes_cancellation_through()
    {
        var distribution = NewDistribution();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => distribution.WaitForBuildAdvanceAsync(
                DateTimeOffset.MinValue, TimeSpan.FromSeconds(5), cts.Token));
    }
}
