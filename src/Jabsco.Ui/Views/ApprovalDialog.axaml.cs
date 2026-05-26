using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Jabsco.Ui.Views;

public partial class ApprovalDialog : Window
{
    public enum Result { AllowOnce, AllowSession, Deny }

    public ApprovalDialog() => InitializeComponent();

    public void SetDetail(string detail)
    {
        if (this.FindControl<TextBlock>("ToolDetailText") is { } tb)
            tb.Text = detail;
    }

    private void AllowOnce_Click(object? sender, RoutedEventArgs e)    => Close(Result.AllowOnce);
    private void AllowSession_Click(object? sender, RoutedEventArgs e) => Close(Result.AllowSession);
    private void Deny_Click(object? sender, RoutedEventArgs e)         => Close(Result.Deny);
}
