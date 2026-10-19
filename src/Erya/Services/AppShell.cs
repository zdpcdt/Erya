using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Avalonia.Controls.Notifications;
using Erya.Core.Distributions;

namespace Erya.Services;

/// <summary>Per-OS install hint shown when no distribution was detected.</summary>
internal static class InstallHints
{
    public static string ForCurrentOs => OperatingSystem.IsWindows()
        ? "请先安装小狼毫（https://rime.im/download），安装完成后重新打开尔雅。"
        : OperatingSystem.IsMacOS()
            ? "请先安装鼠鬚管 Squirrel（https://rime.im/download），安装完成后重新打开尔雅。"
            : "请先安装 fcitx5-rime 或 ibus-rime（发行版软件仓库），安装完成后重新打开尔雅。";

    /// <summary>
    /// Hint shown when a distribution was found but its deployer was not.
    /// Branches on the DISTRIBUTION (not the running OS — in production the
    /// two always match, and distro-based branching keeps the Linux package
    /// hints testable on any dev machine). On Linux this names the distro's
    /// librime tools package, whose name is anything but uniform:
    /// librime-bin (Debian), librime (Arch, Fedora), rime (openSUSE).
    /// </summary>
    public static string DeployerMissingHint(IRimeDistribution? distribution, string? osReleaseContent = null)
        => distribution?.Id switch
        {
            "weasel" => "未找到部署器 WeaselDeployer.exe，无法触发重新部署（小狼毫是否完整安装？）。",
            "squirrel" => "未找到鼠鬚管部署器，无法触发重新部署。",
            "fcitx5-rime" or "ibus-rime" => LinuxDeployerHint(distribution, osReleaseContent),
            _ => OperatingSystem.IsWindows()
                ? "未找到部署器 WeaselDeployer.exe，无法触发重新部署（小狼毫是否完整安装？）。"
                : OperatingSystem.IsMacOS()
                    ? "未找到鼠鬚管部署器，无法触发重新部署。"
                    : LinuxDeployerHint(null, osReleaseContent),
        };

    private static string LinuxDeployerHint(IRimeDistribution? distribution, string? osReleaseContent)
    {
        var missing = distribution?.Id switch
        {
            "fcitx5-rime" => "未找到 rime_deployer，无法触发重新部署。",
            "ibus-rime" => "未找到 ibus 命令或 rime_deployer，无法触发重新部署。",
            _ => "未找到部署工具，无法触发重新部署。",
        };
        var ids = OsRelease.Ids(osReleaseContent ?? OsRelease.ReadFromFile());
        var package = ids switch
        {
            var l when l.Contains("arch") => "sudo pacman -S librime",
            var l when l.Contains("debian") || l.Contains("ubuntu") => "sudo apt install librime-bin",
            var l when l.Contains("fedora") || l.Contains("rhel") || l.Contains("centos") => "sudo dnf install librime",
            var l when l.Any(id => id.Contains("suse")) => "sudo zypper install rime",
            _ => null,
        };
        var install = package is null
            ? "请从软件仓库安装包含 rime_deployer 的 librime 工具包（常见包名：librime、librime-bin、rime）。"
            : $"安装命令：{package}";
        return $"{missing}{install}安装完成后重启尔雅即可。";
    }
}

/// <summary>Parses os-release(5): the ID and ID_LIKE identity fields.</summary>
internal static class OsRelease
{
    private const string OsReleasePath = "/etc/os-release";

    /// <summary>
    /// Identity ids in priority order — ID first, then the ID_LIKE family
    /// tokens (e.g. ubuntu → [ubuntu, debian]). Empty when unavailable.
    /// </summary>
    public static IReadOnlyList<string> Ids(string? content)
    {
        if (string.IsNullOrWhiteSpace(content)) return [];
        var ids = new List<string>();
        foreach (var line in content.Split('\n'))
        {
            var separator = line.IndexOf('=');
            if (separator <= 0) continue;
            var key = line[..separator].Trim();
            if (key is not ("ID" or "ID_LIKE")) continue;

            var value = line[(separator + 1)..].Trim().Trim('"', '\'');
            // ID_LIKE is a space-separated list; ids never contain spaces.
            foreach (var token in value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                if (!ids.Contains(token)) ids.Add(token);
        }
        return ids;
    }

    public static string? ReadFromFile()
    {
        try
        {
            return File.Exists(OsReleasePath) ? File.ReadAllText(OsReleasePath) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}

/// <summary>Cross-platform file-manager entry with a graceful failure path.</summary>
internal static class AppShell
{
    /// <summary>
    /// Opens a folder in the platform file manager (Explorer / Finder /
    /// xdg-open). On Unix the shell handler may be missing on minimal systems,
    /// so failures surface as a toast instead of an unhandled exception.
    /// </summary>
    public static void OpenFolder(string path)
    {
        try
        {
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            AppNotifications.Toast($"无法打开目录：{ex.Message}", NotificationType.Error);
        }
    }
}
