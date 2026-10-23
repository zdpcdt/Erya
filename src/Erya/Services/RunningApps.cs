using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Erya.Services;

/// <summary>正在运行的一条应用：<see cref="Id"/> 是 app_options 的键，<see cref="Title"/> 是展示用友好名（可空）。</summary>
public sealed record RunningApp(string Id, string? Title = null);

/// <summary>
/// 「正在运行的应用」枚举，添加应用弹层的数据源。Windows 列有窗口的进程
/// （exe 名即键）；macOS 问 NSWorkspace 要 Regular 应用（Bundle ID 即键，
/// localizedName 作友好名——绕开「进程名与 Bundle ID 对不上」的进程表）；
/// Linux 为空。任何失败都退回空列表：弹层只少一个区，不炸。
/// </summary>
public static class RunningApps
{
    public static IReadOnlyList<RunningApp> Enumerate()
    {
        try
        {
            return OperatingSystem.IsWindows() ? Windows()
                 : OperatingSystem.IsMacOS() ? Mac.RegularApps()
                 : [];
        }
        catch
        {
            return []; // 枚举被拒绝（权限/进程退出竞态）——弹层只少一个区，不炸
        }
    }

    private static IReadOnlyList<RunningApp> Windows()
        => Process.GetProcesses()
            .Where(p => p.MainWindowHandle != 0 && p.ProcessName is not ("Erya"))
            .Select(p => new RunningApp(p.ProcessName + ".exe"))
            .DistinctBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r.Id, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>
    /// macOS：不引绑定库，直接和 objc runtime 打交道——dlopen AppKit 让
    /// NSWorkspace 类注册进 runtime（宿主没链 AppKit 时 objc_getClass 拿到
    /// nil，发消息静默返回空），再取 sharedWorkspace.runningApplications 逐个
    /// 问属性。全是 public API，无权限弹窗。const char* 参数封 byte[] 手工补
    /// 尾 0，不用 DllImport 字符串封送，Native AOT 下无歧义。
    /// </summary>
    internal static class Mac
    {
        private const string LibSystem = "/usr/lib/libSystem.dylib";
        private const string LibObjC = "/usr/lib/libobjc.dylib";
        private const string LibCF = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string AppKitPath = "/System/Library/Frameworks/AppKit.framework/AppKit";
        private const int RtldLazy = 1;
        private const long Utf8 = 0x08000100; // kCFStringEncodingUTF8

        [DllImport(LibSystem)]
        private static extern IntPtr dlopen(byte[] path, int mode);

        [DllImport(LibObjC)]
        private static extern IntPtr objc_getClass(byte[] name);

        [DllImport(LibObjC)]
        private static extern IntPtr sel_registerName(byte[] name);

        // objc_msgSend 一个入口、按返回类型出多个声明（arm64 下寄存器宽度不同）。
        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        internal static extern IntPtr Send(IntPtr receiver, IntPtr selector);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern UIntPtr SendUint(IntPtr receiver, IntPtr selector);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern long SendLong(IntPtr receiver, IntPtr selector);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern int SendInt(IntPtr receiver, IntPtr selector);

        [DllImport(LibObjC, EntryPoint = "objc_msgSend")]
        private static extern IntPtr SendUintArg(IntPtr receiver, IntPtr selector, UIntPtr arg);

        [DllImport(LibCF)]
        private static extern nint CFStringGetLength(IntPtr cfString);

        [DllImport(LibCF)]
        private static extern nint CFStringGetMaximumSizeForEncoding(nint length, long encoding);

        [DllImport(LibCF)]
        private static extern bool CFStringGetCString(IntPtr cfString, byte[] buffer, nint bufferLength, long encoding);

        internal static IReadOnlyList<RunningApp> RegularApps()
        {
            dlopen(Nz(AppKitPath), RtldLazy);
            var workspace = Send(GetClass("NSWorkspace"), Sel("sharedWorkspace"));
            var apps = Send(workspace, Sel("runningApplications"));

            var objectAtIndex = Sel("objectAtIndex:");
            var bundleSel = Sel("bundleIdentifier");
            var nameSel = Sel("localizedName");
            var policySel = Sel("activationPolicy");
            var pidSel = Sel("processIdentifier");

            var result = new List<RunningApp>();
            var count = (nuint)SendUint(apps, Sel("count"));
            for (nuint i = 0; i < count; i++)
            {
                var app = SendUintArg(apps, objectAtIndex, i);
                // activationPolicy 0=Regular：有 Dock 图标的正常 GUI 应用，
                // 排除菜单栏常驻、后台代理与自己。
                if (SendLong(app, policySel) != 0) continue;
                if (SendInt(app, pidSel) == Environment.ProcessId) continue;
                var id = ToManaged(Send(app, bundleSel));
                if (string.IsNullOrEmpty(id)) continue;
                result.Add(new RunningApp(id, ToManaged(Send(app, nameSel))));
            }
            return result.OrderBy(r => r.Title ?? r.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static IntPtr GetClass(string name) => objc_getClass(Nz(name));

        private static IntPtr Sel(string name) => sel_registerName(Nz(name));

        /// <summary>C 的 const char* 要尾 0。</summary>
        private static byte[] Nz(string s) => [.. Encoding.UTF8.GetBytes(s), 0];

        /// <summary>NSString 与 CFStringRef toll-free bridged，按 CFString 转 UTF-8。</summary>
        private static string? ToManaged(IntPtr cfString)
        {
            if (cfString == IntPtr.Zero) return null;
            var max = CFStringGetMaximumSizeForEncoding(CFStringGetLength(cfString), Utf8);
            var buffer = new byte[(int)max + 1];
            if (!CFStringGetCString(cfString, buffer, buffer.Length, Utf8)) return null;
            var end = Array.IndexOf(buffer, (byte)0);
            return Encoding.UTF8.GetString(buffer, 0, end < 0 ? buffer.Length : end);
        }
    }
}
