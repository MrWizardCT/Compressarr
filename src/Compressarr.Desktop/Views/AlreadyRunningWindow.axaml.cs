using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;

namespace Compressarr.Desktop.Views;

public partial class AlreadyRunningWindow : Window
{
    public AlreadyRunningWindow()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOkClicked(object? sender, RoutedEventArgs e) => Close();
}
