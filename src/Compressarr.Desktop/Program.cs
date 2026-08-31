using Avalonia;
using System;

namespace Compressarr.Desktop;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        // Checked before anything Avalonia-related so a blocked second launch never builds the
        // normal DI/Kestrel/tray stack or binds the configured port - it just shows one small
        // window and exits.
        if (!SingleInstanceLock.TryAcquire())
        {
            BuildAlreadyRunningApp().StartWithClassicDesktopLifetime(args);
            return;
        }

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

    private static AppBuilder BuildAlreadyRunningApp()
        => AppBuilder.Configure<AlreadyRunningApp>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
