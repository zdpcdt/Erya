using System;
using System.Diagnostics;
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

    /// <summary>Per-OS hint shown when a distribution was found but its deployer was not.</summary>
    public static string DeployerMissingHint => OperatingSystem.IsWindows()
        ? "未找到部署器 WeaselDeployer.exe，无法触发重新部署。"
        : OperatingSystem.IsMacOS()
            ? "未找到鼠鬚管部署器，无法触发重新部署。"
            : "未找到 rime_deployer（librime-bin 包）或 ibus，无法触发重新部署。";
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
