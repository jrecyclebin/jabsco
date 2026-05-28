using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace Jabsco.Ui.Views;

public partial class ConfirmDialog : Window
{
    public ConfirmDialog() => InitializeComponent(); // satisfies Avalonia XAML loader

    public ConfirmDialog(string title, string message, string confirmLabel)
    {
        InitializeComponent();
        Title = title;
        if (this.FindControl<TextBlock>("MessageText") is { } tb) tb.Text = message;
        if (this.FindControl<Button>("ConfirmBtn") is { } btn) btn.Content = confirmLabel;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e) => Close(true);
    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(false);

    protected override void OnKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { Close(false); e.Handled = true; }
        base.OnKeyDown(e);
    }
}
