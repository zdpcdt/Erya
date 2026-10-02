using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Erya.Core.Distributions;
using Erya.Views;
using Erya.ViewModels;
using Ursa.Controls;

namespace Erya;

public partial class MainWindow : UrsaWindow
{
    private readonly MainViewModel _overview;
    private readonly SchemaManagerViewModel _schemas;
    private readonly Dictionary<string, Control> _pages;
    private bool _schemasLoaded;

    /// <summary>For the XAML runtime loader / previewer: shows the "no distribution" state.</summary>
    public MainWindow() : this(new DistributionManager()) { }

    public MainWindow(DistributionManager distributions)
    {
        InitializeComponent();
        _overview = new MainViewModel(distributions);
        _schemas = new SchemaManagerViewModel(_overview.Distribution);
        _pages = new Dictionary<string, Control>
        {
            ["overview"] = new OverviewPage { DataContext = _overview },
            ["schemas"] = new SchemaManagerPage { DataContext = _schemas },
        };
        PageHost.Content = _pages["overview"];
    }

    private void OnNavSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavMenuItem { Tag: string key } item
            || !_pages.TryGetValue(key, out var page)) return;

        PageHost.Content = page;
        PageTitleText.Text = item.Header?.ToString() ?? key;

        if (key == "schemas" && !_schemasLoaded)
        {
            _schemasLoaded = true;
            _schemas.LoadCommand.Execute(null);
        }
    }
}
