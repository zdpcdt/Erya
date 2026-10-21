using Ursa.Controls;

namespace Erya;

public partial class MainWindow : UrsaWindow
{
    // AppNotifications resolves these from the window's visual tree; UrsaWindow
    // does not auto-install them, and without an instance every Toast/Notify
    // call silently drops. Fields keep the managers alive.
    private readonly WindowToastManager _toastManager;
    private readonly WindowNotificationManager _notificationManager;

    public MainWindow()
    {
        InitializeComponent();
        _toastManager = new WindowToastManager(this);
        _notificationManager = new WindowNotificationManager(this);
    }
}
