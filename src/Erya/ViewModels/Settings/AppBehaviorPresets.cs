using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace Erya.ViewModels.Settings;

/// <summary>添加应用对话框里的一条常用应用预设。</summary>
public sealed record AppPresetItem(string Id, string Title);

/// <summary>
/// 常用应用预设与「正在运行的应用」枚举。预设挑的是实际需要 app_options 的场景
/// ——终端、远程、控制台编辑器这类敲命令多、中文少的应用；不做大而全的应用商店。
/// </summary>
public static class AppBehaviorPresets
{
    /// <summary>Windows（小狼毫）：app_options 按进程 exe 名匹配。</summary>
    public static readonly IReadOnlyList<AppPresetItem> Windows =
    [
        new("cmd.exe", "命令提示符"),
        new("conhost.exe", "传统控制台宿主"),
        new("powershell.exe", "Windows PowerShell"),
        new("pwsh.exe", "PowerShell 7"),
        new("WindowsTerminal.exe", "Windows 终端"),
        new("mintty.exe", "MinTTY（Git Bash）"),
        new("putty.exe", "PuTTY"),
        new("ssh.exe", "OpenSSH 客户端"),
        new("gvim.exe", "Vim（gvim）"),
        new("mstsc.exe", "远程桌面连接"),
    ];

    /// <summary>macOS（鼠须管）：app_options 按应用 Bundle ID 匹配。</summary>
    public static readonly IReadOnlyList<AppPresetItem> MacOS =
    [
        new("com.apple.Terminal", "终端（Terminal）"),
        new("com.googlecode.iterm2", "iTerm2"),
        new("net.kovidgoyal.kitty", "kitty"),
        new("com.mitchellh.ghostty", "Ghostty"),
        new("com.jetbrains.intellij", "IntelliJ IDEA"),
        new("com.microsoft.rdc.macos", "Windows 远程桌面"),
    ];

    public static IReadOnlyList<AppPresetItem> ForCurrentOs()
        => OperatingSystem.IsWindows() ? Windows
         : OperatingSystem.IsMacOS() ? MacOS
         : [];

    /// <summary>
    /// 正在运行的应用（仅 Windows 有意义：进程 exe 名就是 app_options 的键；
    /// macOS 进程名与 Bundle ID 对不上，不提供）。列有窗口的进程，排除自身。
    /// </summary>
    public static IReadOnlyList<string> RunningWindowsApps()
    {
        if (!OperatingSystem.IsWindows()) return [];
        try
        {
            return Process.GetProcesses()
                .Where(p => p.MainWindowHandle != 0 && p.ProcessName is not ("Erya"))
                .Select(p => p.ProcessName + ".exe")
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        catch
        {
            return []; // 进程枚举被拒绝（权限/进程退出竞态）——对话框只少一个区，不炸
        }
    }
}
