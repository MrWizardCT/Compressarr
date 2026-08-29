using Avalonia.Controls;
using Avalonia.Interactivity;
using Compressarr.Desktop.ViewModels;

namespace Compressarr.Desktop.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private async void OnRemoveLaneClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control { DataContext: LaneViewModel lane }) return;
        if (DataContext is not MainViewModel mainViewModel) return;

        var confirmed = await ConfirmDialog.AskAsync(this,
            $"Remove lane '{lane.DisplayName}'?\n\nThis only removes it from Compressarr's configuration - no files are touched. Click Save Settings afterward to persist the change.",
            confirmText: "Remove", title: "Remove Lane");

        if (confirmed)
        {
            mainViewModel.RemoveLane(lane);
        }
    }

    private async void OnInstallPresetsClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel mainViewModel) return;

        if (mainViewModel.PresetsFileNeedsMergePrompt())
        {
            var confirmed = await ConfirmDialog.AskAsync(this,
                "A presets.json already exists at the configured path.\n\nMerge Compressarr's two presets (\"Compressarr SD-HD\" and \"Compressarr UHD AV1\") into it? Every other preset already in that file is left untouched.",
                confirmText: "Merge", title: "Merge Compressarr Presets");

            if (confirmed)
            {
                mainViewModel.MergePresets();
            }
        }
        else
        {
            mainViewModel.InstallPresetsFresh();
        }
    }
}
