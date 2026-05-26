using CommunityToolkit.Mvvm.ComponentModel;

namespace Jabsco.Ui.ViewModels;

public partial class SessionTabViewModel : ViewModelBase
{
    [ObservableProperty]
    private bool _isActive;

    public string Title { get; }
    public SessionViewModel Session { get; }

    public SessionTabViewModel(SessionViewModel session)
    {
        Session = session;
        Title = session.Host;
    }
}
