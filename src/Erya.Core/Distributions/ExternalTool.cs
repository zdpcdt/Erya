using System.Diagnostics;

namespace Erya.Core.Distributions;

/// <summary>
/// Locates executables on PATH and runs them to completion, surfacing
/// non-zero exits as exceptions with the captured stderr. Used for the
/// Unix deploy entries (rime_deployer, ibus, Squirrel).
/// </summary>
public static class ExternalTool
{
    public static string? FindOnPath(string name)
    {
        var pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathEnv)) return null;

        var separator = OperatingSystem.IsWindows() ? ';' : ':';
        string[] suffixes = OperatingSystem.IsWindows() ? ["", ".exe", ".bat", ".cmd"] : [""];
        foreach (var dir in pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            foreach (var suffix in suffixes)
            {
                var candidate = Path.Combine(dir, name + suffix);
                if (File.Exists(candidate)) return candidate;
            }

        return null;
    }

    /// <summary>Runs the tool to completion; throws on non-zero exit with the captured stderr.</summary>
    public static async Task RunAsync(string path, IReadOnlyList<string> arguments, CancellationToken cancellationToken = default)
    {
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo);
        if (process is null)
            throw new InvalidOperationException($"无法启动进程：{path}");

        var stderr = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);
        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"{Path.GetFileName(path)} 退出码 {process.ExitCode}：{stderr.Result.Trim()}");
    }
}
