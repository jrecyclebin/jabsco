using Avalonia.Controls;
using Jabsco.Ui.ViewModels;

namespace Jabsco.Ui.Views;

public partial class MainWindow : Window
{
    private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ConfirmCloseTab = async () =>
            {
                var dialog = new ConfirmDialog(
                    "Close Session",
                    "Close this session? The conversation cannot be resumed after disconnecting.",
                    "Disconnect");
                return await dialog.ShowDialog<bool>(this);
            };
        }
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        base.OnClosing(e);
        if (_closeConfirmed || DataContext is not MainWindowViewModel { SessionTabs.Count: > 0 } vm)
            return;

        e.Cancel = true;
        _ = ConfirmAndCloseAsync(vm.SessionTabs.Count);
    }

    private async Task ConfirmAndCloseAsync(int count)
    {
        var msg = count == 1
            ? "Close Jabsco? Your open session will be disconnected. The conversation cannot be resumed."
            : $"Close Jabsco? You have {count} open sessions. All conversations will be lost and cannot be resumed.";
        var dialog = new ConfirmDialog("Close Jabsco", msg, "Quit");
        var confirmed = await dialog.ShowDialog<bool>(this);
        if (confirmed)
        {
            _closeConfirmed = true;
            Close();
        }
    }
}
