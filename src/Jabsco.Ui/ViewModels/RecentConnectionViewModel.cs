using CommunityToolkit.Mvvm.ComponentModel;

namespace Jabsco.Ui.ViewModels;

public partial class RecentConnectionViewModel : ObservableObject
{
    public int ProfileId { get; init; }
    public string Name { get; init; } = "";
    public string Host { get; init; } = "";
    public int Port { get; init; } = 3389;
    public string? Username { get; init; }
    public Guid? VmId { get; init; }
    public string Transport { get; init; } = "tcp";
    public bool IsHyperV => string.Equals(Transport, "hvsocket", StringComparison.OrdinalIgnoreCase);
    public DateTimeOffset LastUsed { get; init; }
    public string LastUsedDisplay => LastUsed.ToLocalTime().ToString("MMM d, h:mm tt");

    [ObservableProperty] private bool _isPendingDelete;
}
