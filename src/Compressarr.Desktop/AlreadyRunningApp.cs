using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Compressarr.Desktop.Views;

namespace Compressarr.Desktop;

/// <summary>Taken instead of the normal App when SingleInstanceLock.TryAcquire() fails - shows a
/// single "already running" window and nothing else. Builds none of the normal DI/Kestrel/tray
/// stack, so a blocked second launch never binds the configured port.</summary>
public partial class AlreadyRunningApp : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new AlreadyRunningWindow();
        }

        base.OnFrameworkInitializationCompleted();
    }
}
