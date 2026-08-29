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
            $"Remove lane '{lane.DisplayName}'?\n\nThis only removes it from Compressarr's configuration - no files are touched. Click Save Settings afterward to persist the change.");

        if (confirmed)
        {
            mainViewModel.RemoveLane(lane);
        }
    }
}
