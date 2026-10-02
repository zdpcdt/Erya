using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Erya.Core.Distributions;
using Erya.Core.Distributions.Weasel;

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
            // in per-OS locators, composed here in priority order. New
            // frontends join as their locators land (Squirrel, fcitx5-rime…).
            var locators = new List<IRimeDistributionLocator>();
            if (OperatingSystem.IsWindows()) locators.Add(new WeaselLocator());
            // Day 6: OperatingSystem.IsLinux() → Fcitx5RimeLocator, IbusRimeLocator
            // macOS:  SquirrelLocator

            desktop.MainWindow = new MainWindow(new DistributionManager([.. locators]));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
