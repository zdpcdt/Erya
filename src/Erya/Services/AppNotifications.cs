using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Ursa.Controls;
using UrsaNotification = Ursa.Controls.Notification;
using UrsaWindowNotificationManager = Ursa.Controls.WindowNotificationManager;

namespace Erya.Services;

/// <summary>
/// Bridges view models to Ursa's toast / notification managers installed on
/// the main window. Channel conventions:
/// <list type="bullet">
/// <item>Toast — short transient feedback for a user-initiated action.</item>
/// <item>Notification — richer (title + body) and slightly longer lived; used
/// for outcomes that must not be missed.</item>
/// <item>Persistent state (e.g. detection result) belongs in a Banner, not here.</item>
/// </list>
/// </summary>
internal static class AppNotifications
{
    public static void Toast(string content, NotificationType type, TimeSpan? expiration = null)
    {
        var window = MainWindowOrNull();
        if (window is null) return;
        if (!WindowToastManager.TryGetToastManager(window, out var manager) || manager is null) return;
        // The "Light" class pins the card to the Semi theme's light palette
        // (solid background + border) regardless of the app theme variant.
        manager.Show(content, type, expiration ?? TimeSpan.FromSeconds(4),
            showIcon: true, showClose: true, onClick: null, onClose: null, classes: ["Light"]);
    }

    public static void Notify(string title, string content, NotificationType type)
    {
        var window = MainWindowOrNull();
        if (window is null) return;
        if (!UrsaWindowNotificationManager.TryGetNotificationManager(window, out var manager) || manager is null) return;
        manager.Position = NotificationPosition.TopRight;
        manager.Show(new UrsaNotification(title, content, type, TimeSpan.FromSeconds(6),
            showClose: true, onClick: null, onClose: null));
    }

    private static TopLevel? MainWindowOrNull()
        => (Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime)?.MainWindow;

    /// <summary>主窗口（窗口级 Dialog 的 owner 等）。</summary>
    public static Window? MainWindow => MainWindowOrNull() as Window;

    /// <summary>主窗口剪贴板；无窗口时（无头测试）为 null。</summary>
    public static Avalonia.Input.Platform.IClipboard? ClipboardOrNull()
        => MainWindowOrNull()?.Clipboard;
}
