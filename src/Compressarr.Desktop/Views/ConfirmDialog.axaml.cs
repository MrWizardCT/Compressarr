using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Compressarr.Desktop.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    public static Task<bool> AskAsync(Window owner, string message, string confirmText = "OK", string title = "Confirm")
    {
        var dialog = new ConfirmDialog { Title = title };
        dialog.FindControl<TextBlock>("MessageText")!.Text = message;
        dialog.FindControl<Button>("ConfirmButton")!.Content = confirmText;
        return dialog.ShowDialog<bool>(owner);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close(false);

    private void OnConfirmClick(object? sender, RoutedEventArgs e) => Close(true);
}
