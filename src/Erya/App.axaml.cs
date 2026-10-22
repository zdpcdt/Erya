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
                // Linux 前端（fcitx5/ibus）的候选面板由输入法框架自己绘制，
                // 没有 squirrel/weasel 那份外观 YAML → 外观设置整组不显示。
                DataContext = new ShellViewModel(new DistributionManager([.. locators]),
                    showAppearance: !OperatingSystem.IsLinux()),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
