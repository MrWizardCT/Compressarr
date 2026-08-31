using System.Diagnostics;
using Compressarr.Core.Config;
using Compressarr.Core.Orchestration;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Compressarr.Desktop.ViewModels;

/// <summary>Backs the system tray icon's NativeMenu - the only UI Compressarr.Desktop has left.
/// Everything else lives in the browser; this just starts/stops monitoring and opens it.</summary>
public partial class AppTrayViewModel : ObservableObject
{
    private readonly IConfigStore _configStore;
    private readonly IRunLoopController _runLoopController;
    private readonly string _webUrl;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStopMonitoring))]
    public partial bool IsMonitoring { get; set; }

    // Mirrors IRunLoopController.IsStopping/StoppingChanged - the shared source of truth both
    // this tray menu and the web UI observe, so a stop requested from either surface (not just
    // this tray's own Stop Monitoring click) is reflected here too.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStopMonitoring))]
    [NotifyPropertyChangedFor(nameof(StopMenuHeader))]
    public partial bool IsStopping { get; set; }

    public bool CanStopMonitoring => IsMonitoring && !IsStopping;
    public string StopMenuHeader => IsStopping ? "Stopping after task" : "Stop Monitoring";

    public AppTrayViewModel(IConfigStore configStore, IRunLoopController runLoopController, string webUrl)
    {
        _configStore = configStore;
        _runLoopController = runLoopController;
        _webUrl = webUrl;

        _runLoopController.RunningChanged += running => IsMonitoring = running;
        _runLoopController.StoppingChanged += stopping => IsStopping = stopping;
        IsMonitoring = _runLoopController.IsRunning;
        IsStopping = _runLoopController.IsStopping;
    }

    [RelayCommand]
    private void OpenWebUi()
    {
        Process.Start(new ProcessStartInfo(_webUrl) { UseShellExecute = true });
    }

    [RelayCommand]
    private void StartMonitoring()
    {
        var config = _configStore.Load(AppPaths.GetConfigFilePath());
        _runLoopController.Start(config, TimeSpan.FromSeconds(Math.Max(5, config.Repeat.PollIntervalSeconds)));
    }

    [RelayCommand]
    private async Task StopMonitoringAsync()
    {
        // IsStopping is driven entirely by StoppingChanged (subscribed in the constructor) so it
        // reflects a stop requested from either surface, not just this command's own click.
        await _runLoopController.StopAsync();
    }

    [RelayCommand]
    private void Exit()
    {
        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.Shutdown();
        }
    }
}
