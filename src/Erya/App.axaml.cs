using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Erya.Core.Distributions;
using Erya.Core.Distributions.Weasel;
using Erya.ViewModels;

namespace Erya;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // New frontends join the locator list here as they land
            // (fcitx5-rime / ibus-rime, Squirrel, ...).
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainViewModel(new DistributionManager(new WeaselLocator())),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
