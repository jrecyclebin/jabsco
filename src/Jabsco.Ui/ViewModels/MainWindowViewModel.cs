using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jabsco.Core.Config;
using Jabsco.Core.Credentials;
using Jabsco.Core.Persistence;

namespace Jabsco.Ui.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    public ObservableCollection<SessionTabViewModel> SessionTabs { get; } = [];

    [ObservableProperty]
    private SessionTabViewModel? _activeTab;

    [ObservableProperty]
    private bool _isConnectionPanelActive = true;

    public ConnectionPanelViewModel ConnectionPanel { get; }

    public MainWindowViewModel(FeatureFlags? features = null)
    {
        ConnectionPanel = new ConnectionPanelViewModel(this, features ?? new FeatureFlags());
    }

    public async Task InitializeAsync()
    {
        var db = await JabscoDb.OpenAsync();
        ICredentialStore credentials = CreateCredentialStore();
        await ConnectionPanel.LoadAsync(db, credentials);
    }

    private static ICredentialStore CreateCredentialStore()
    {
        if (OperatingSystem.IsWindows()) return new DpapiCredentialStore();
        if (OperatingSystem.IsLinux())   return new LibsecretCredentialStore();
        if (OperatingSystem.IsMacOS())   return new MacosCredentialStore();
        throw new PlatformNotSupportedException("Jabsco requires Windows, Linux, or macOS");
    }

    internal void AddSession(SessionTabViewModel tab)
    {
        // Wire disconnect so closing the session auto-removes the tab
        tab.Session.CloseTab = () => Avalonia.Threading.Dispatcher.UIThread.Post(() => CloseTab(tab));

        SessionTabs.Add(tab);
        SelectTab(tab);
    }

    [RelayCommand]
    private void ShowConnectionPanel()
    {
        if (ActiveTab != null)
            ActiveTab.IsActive = false;
        ActiveTab = null;
        IsConnectionPanelActive = true;
    }

    [RelayCommand]
    private void SelectTab(SessionTabViewModel tab)
    {
        if (ActiveTab != null)
            ActiveTab.IsActive = false;

        ActiveTab = tab;
        tab.IsActive = true;
        IsConnectionPanelActive = false;
    }

    [RelayCommand]
    private void CloseTab(SessionTabViewModel tab)
    {
        tab.Session.CloseTab = null;
        _ = tab.Session.DisposeAsync();
        SessionTabs.Remove(tab);

        if (ActiveTab == tab)
        {
            tab.IsActive = false;
            if (SessionTabs.Count > 0)
                SelectTab(SessionTabs[^1]);
            else
                ShowConnectionPanel();
        }
    }
}
