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

    // True from the moment Stop Monitoring is clicked until StopAsync actually resolves - it
    // doesn't resolve (and IsMonitoring doesn't flip) until the in-flight file finishes
    // converting, which can be minutes away. Without this, the tray menu gives no indication the
    // click registered at all.
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStopMonitoring))]
    [NotifyPropertyChangedFor(nameof(StopMenuHeader))]
    public partial bool IsStopping { get; set; }

    public bool CanStopMonitoring => IsMonitoring && !IsStopping;
    public string StopMenuHeader => IsStopping ? "Stopping..." : "Stop Monitoring";

    public AppTrayViewModel(IConfigStore configStore, IRunLoopController runLoopController, string webUrl)
    {
        _configStore = configStore;
        _runLoopController = runLoopController;
        _webUrl = webUrl;

        _runLoopController.RunningChanged += running => IsMonitoring = running;
        IsMonitoring = _runLoopController.IsRunning;
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
        IsStopping = true;
        await _runLoopController.StopAsync();
        IsStopping = false;
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
