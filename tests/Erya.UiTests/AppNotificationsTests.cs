using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Notifications;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Erya;
using Erya.Core.Distributions;
using Erya.Services;
using Erya.ViewModels;
using Ursa.Controls;
using Xunit;

namespace Erya.UiTests;

/// <summary>
/// AppNotifications resolves toast/notification managers from the main
/// window's visual tree. UrsaWindow does not auto-install them, so MainWindow
/// must attach them itself — otherwise every save/deploy Toast and Notify
/// call silently drops on every platform. These tests drive the real
/// AppNotifications entry points against the real MainWindow.
/// </summary>
public class AppNotificationsTests
{
    private static MainWindow ShowMainWindow()
    {
        // Seed nav icon resources the same way ShellNavTests does; headless
        // sessions don't populate App.axaml resources on their own.
        var icons = new ResourceDictionary();
        foreach (var key in new[] { "SemiIconHome", "SemiIconCheckList", "SemiIconSetting",
                     "SemiIconColorPalette", "SemiIconCode", "SemiIconRestore" })
            icons[key] = new StreamGeometry();
        Application.Current!.Resources.MergedDictionaries.Add(icons);

        var window = new MainWindow { DataContext = new ShellViewModel(new DistributionManager()) };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // AppNotifications resolves the window through the desktop lifetime,
        // which headless sessions leave null (and which can't be swapped
        // through the guarded setter once the App is initialized) — seed the
        // backing field with a lifetime holding our window.
        typeof(Application).GetField("_applicationLifetime",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(Application.Current,
                new ClassicDesktopStyleApplicationLifetime { MainWindow = window });
        return window;
    }

    [AvaloniaFact]
    public void MainWindow_installs_toast_and_notification_managers()
    {
        var window = ShowMainWindow();
        Assert.True(WindowToastManager.TryGetToastManager(window, out _),
            "MainWindow must install a WindowToastManager (UrsaWindow does not auto-attach one).");
        Assert.True(Ursa.Controls.WindowNotificationManager.TryGetNotificationManager(window, out _),
            "MainWindow must install a WindowNotificationManager (UrsaWindow does not auto-attach one).");
    }

    [AvaloniaFact]
    public void Toast_shows_a_sized_Light_card_in_the_visual_tree()
    {
        var window = ShowMainWindow();

        AppNotifications.Toast("设置已保存，重新部署完成", NotificationType.Success);
        Dispatcher.UIThread.RunJobs();
        // The Semi toast theme opens with a ScaleY 0→1 entrance animation on a
        // LayoutTransformControl, which participates in measure. RunJobs alone
        // never ticks the headless render clock, leaving the card at its
        // zero-height first frame — sleep past the 0.3s animation and force a
        // tick before asserting on size.
        Thread.Sleep(600);
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        var card = window.GetVisualDescendants()
            .Single(d => d.GetType().Name == "ToastCard");
        var control = Assert.IsAssignableFrom<Control>(card);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0,
            $"toast card should have non-zero size after the entrance animation, got {control.Bounds}");
        Assert.Contains("Light", control.Classes); // pinned light palette
    }

    [AvaloniaFact]
    public void Notification_shows_a_sized_card_in_the_visual_tree()
    {
        var window = ShowMainWindow();

        AppNotifications.Notify("设置已保存", "已写入 default.custom.yaml。", NotificationType.Warning);
        Dispatcher.UIThread.RunJobs();
        Thread.Sleep(600);
        window.CaptureRenderedFrame();
        Dispatcher.UIThread.RunJobs();

        var card = window.GetVisualDescendants()
            .Single(d => d.GetType().Name == "NotificationCard");
        var control = Assert.IsAssignableFrom<Control>(card);
        Assert.True(control.Bounds.Width > 0 && control.Bounds.Height > 0,
            $"notification card should have non-zero size after the entrance animation, got {control.Bounds}");
    }
}
