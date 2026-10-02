using Avalonia.Controls;
using Avalonia.Interactivity;
using Erya.ViewModels;

namespace Erya;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnManageSchemasClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel main || main.Distribution is null) return;

        var dialog = new SchemaManagerWindow
        {
            DataContext = new SchemaManagerViewModel(main.Distribution),
        };
        var result = await dialog.ShowDialog<(Avalonia.Controls.Notifications.NotificationType Type, string Message)?>(this);
        if (result is { } status) main.ShowStatus(status.Type, status.Message);
    }
}
