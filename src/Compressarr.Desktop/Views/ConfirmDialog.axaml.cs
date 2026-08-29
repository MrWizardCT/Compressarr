using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Compressarr.Desktop.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static Task<bool> AskAsync(Window owner, string message)
    {
        var dialog = new ConfirmDialog();
        dialog.FindControl<TextBlock>("MessageText")!.Text = message;
        return dialog.ShowDialog<bool>(owner);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
