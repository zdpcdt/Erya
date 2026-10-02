using Avalonia.Controls;
using Avalonia.Interactivity;
using Erya.ViewModels;

namespace Erya;

public partial class SchemaManagerWindow : Window
{
    public SchemaManagerWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await ((SchemaManagerViewModel)DataContext!).LoadAsync();
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private async void OnSaveClick(object? sender, RoutedEventArgs e)
    {
        var result = await ((SchemaManagerViewModel)DataContext!).SaveAndDeployAsync();
        if (result is not null) Close(result);
    }
}
