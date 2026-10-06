using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Erya.Core.Distributions;
using Erya.Core.Distributions.Linux;
using Erya.Core.Distributions.MacOS;
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
            // One entry point for every desktop OS: platform differences live
            // in per-OS locators, composed here in priority order (multiple
            // frontends can coexist on Linux; fcitx5 wins).
            var locators = new List<IRimeDistributionLocator>();
            if (OperatingSystem.IsWindows()) locators.Add(new WeaselLocator());
            if (OperatingSystem.IsLinux())
            {
                locators.Add(new Fcitx5RimeLocator());
                locators.Add(new IbusRimeLocator());
            }
            if (OperatingSystem.IsMacOS()) locators.Add(new SquirrelLocator());

            desktop.MainWindow = new MainWindow
            {
                DataContext = new ShellViewModel(new DistributionManager([.. locators])),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
