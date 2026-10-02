using Avalonia;
using Avalonia.Threading;
using System;
using System.IO;

namespace Erya;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Keep the app alive on unhandled UI exceptions: log and continue,
        // a config tool must not die to the desktop on a single bad click.
        Dispatcher.UIThread.UnhandledException += (_, e) =>
        {
            try
            {
                var logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Erya", "crash.log");
                Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
                File.AppendAllText(logPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} (UI thread){Environment.NewLine}{e.Exception}{Environment.NewLine}{Environment.NewLine}");
            }
            catch
            {
                // Logging must never be the thing that crashes.
            }
            e.Handled = true;
        };

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
#if DEBUG
            .WithDeveloperTools()
#endif
            .WithInterFont()
            .LogToTrace();
}
