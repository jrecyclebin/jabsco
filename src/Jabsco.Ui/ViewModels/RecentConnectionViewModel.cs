using CommunityToolkit.Mvvm.ComponentModel;

namespace Jabsco.Ui.ViewModels;

public partial class RecentConnectionViewModel : ObservableObject
{
    public int ProfileId { get; init; }
    public string Name { get; init; } = "";
    public string Host { get; init; } = "";
    public string? Username { get; init; }
    public DateTimeOffset LastUsed { get; init; }
    public string LastUsedDisplay => LastUsed.ToLocalTime().ToString("MMM d, h:mm tt");

    [ObservableProperty] private bool _isPendingDelete;
}
