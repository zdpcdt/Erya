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
        {
            // Some tools (rime_deployer among them) fail without writing to
            // stderr; without this the message would end in a bare "：" .
            var stderrText = (await stderr).Trim();
            throw new InvalidOperationException(stderrText.Length > 0
                ? $"{Path.GetFileName(path)} 退出码 {process.ExitCode}：{stderrText}"
                : $"{Path.GetFileName(path)} 退出码 {process.ExitCode}（工具未输出错误信息）");
        }
    }

    /// <summary>
    /// Runs the tool to completion and captures stdout without throwing —
    /// for best-effort probes (package-manager queries) where a non-zero
    /// exit simply means "no answer". Synchronous on purpose: callers are
    /// construction-time locators, and sync Process IO avoids sync-over-async.
    /// </summary>
    public static (int ExitCode, string Output) RunCapture(string path, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo(path)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"无法启动进程：{path}");
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return (process.ExitCode, output.Trim());
    }
}
