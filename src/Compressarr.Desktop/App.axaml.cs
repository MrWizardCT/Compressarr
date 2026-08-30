using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Compressarr.Core;
using Compressarr.Core.Config;
using Compressarr.Core.Notifications;
using Compressarr.Desktop.ViewModels;
using Compressarr.Web;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Compressarr.Desktop;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    private WebApplication? _webApp;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // A lightweight, direct config read (same pattern the old MainViewModel's design-time
        // constructor used) just to know which port to bind before the DI container exists -
        // MainViewModel's replacement endpoints reload config themselves per-request afterward.
        var earlyConfig = new JsonConfigStore().Load(AppPaths.GetConfigFilePath());
        var port = earlyConfig.Web.Port;
        var webUrl = $"http://localhost:{port}/";

        // ContentRootPath must be pinned to the assembly's own directory, not left to default to
        // Directory.GetCurrentDirectory() - `dotnet run` sets the working directory to the
        // project's *source* folder (no wwwroot there at all; wwwroot only exists in the build
        // output), which made UseStaticFiles()/UseDefaultFiles() 404 on everything when launched
        // that way. AppContext.BaseDirectory always points at the running assembly's own folder
        // regardless of how the process was started (dotnet run, double-click, Start-Process with
        // no explicit working directory, etc).
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ContentRootPath = AppContext.BaseDirectory
        });
        builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

        builder.Services.AddCompressarrCore();
        builder.Services.AddCompressarrWeb();
        builder.Services.AddSingleton(sp => new AppTrayViewModel(
            sp.GetRequiredService<IConfigStore>(),
            sp.GetRequiredService<Compressarr.Core.Orchestration.IRunLoopController>(),
            webUrl));
#if WINDOWS
        builder.Services.AddSingleton<INotificationService, Notifications.WindowsNotificationService>();
#endif

        _webApp = builder.Build();
        _webApp.UseDefaultFiles();
        _webApp.UseStaticFiles(new Microsoft.AspNetCore.Builder.StaticFileOptions
        {
            // No explicit Cache-Control means browsers fall back to heuristic caching off
            // Last-Modified, which can and does serve a stale wwwroot page after a rebuild - a
            // real trap during local iteration (and for the same reason, LAN clients checking a
            // long-running instance). This app is served from a single local Kestrel instance
            // with no CDN in front of it, so there's no caching benefit worth trading staleness
            // for.
            OnPrepareResponse = ctx =>
            {
                ctx.Context.Response.Headers.CacheControl = "no-cache, no-store, must-revalidate";
            }
        });
        _webApp.MapCompressarrEndpoints();

        Services = _webApp.Services;

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            desktop.ShutdownRequested += (_, _) => _webApp.StopAsync().GetAwaiter().GetResult();

            // TrayIcon is declared in App.axaml via the Application-level TrayIcon.Icons attached
            // property, so it inherits its DataContext from the Application's own logical tree -
            // setting it here is what makes the NativeMenuItem Command bindings resolve.
            DataContext = Services.GetRequiredService<AppTrayViewModel>();
        }

        _ = WebMonitorStartup.StartAsync(_webApp, Services.GetRequiredService<Compressarr.Core.Logging.IRunLogger>());

        if (earlyConfig.Repeat.Monitor)
        {
            var loopController = Services.GetRequiredService<Compressarr.Core.Orchestration.IRunLoopController>();
            loopController.Start(earlyConfig, TimeSpan.FromSeconds(Math.Max(5, earlyConfig.Repeat.PollIntervalSeconds)));
        }

        base.OnFrameworkInitializationCompleted();
    }
}
