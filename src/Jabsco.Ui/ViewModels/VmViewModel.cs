using CommunityToolkit.Mvvm.ComponentModel;
using Jabsco.Core.VmHost;

namespace Jabsco.Ui.ViewModels;

public enum VmBusy { None, Connecting, Starting, Stopping }

public partial class VmViewModel : ObservableObject
{
    public Guid Id { get; }

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect), nameof(CanStart), nameof(CanStop))]
    [NotifyPropertyChangedFor(nameof(CanPressConnect), nameof(CanPressStart), nameof(CanPressStop))]
    [NotifyPropertyChangedFor(nameof(StateGlyph), nameof(StateLabel), nameof(StateColor))]
    private VmState _state;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPressConnect), nameof(CanPressStart), nameof(CanPressStop))]
    [NotifyPropertyChangedFor(nameof(IsStopForcing), nameof(StopLabel))]
    private VmBusy _busy;

    public VmViewModel(VmInfo vm)
    {
        Id = vm.Id;
        _name = vm.Name;
        _state = vm.State;
    }

    // Refresh from the host poll. A real state change clears any pending action —
    // whatever start/stop we were waiting on has resolved.
    public void Update(VmInfo vm)
    {
        Name = vm.Name;
        if (State != vm.State)
        {
            State = vm.State;
            Busy = VmBusy.None;
        }
    }

    // Connect needs a live VM; start/stop track the inverse halves of the lifecycle.
    public bool CanConnect => State is VmState.Running or VmState.Paused;
    public bool CanStart   => State is VmState.Stopped;
    public bool CanStop    => State is VmState.Running or VmState.Paused;

    // Only one action at a time: while an action is underway the row's other buttons are disabled.
    public bool CanPressConnect => CanConnect && Busy == VmBusy.None;
    public bool CanPressStart   => CanStart && Busy == VmBusy.None;
    // Pressable when idle, or while a graceful shutdown is underway — that press escalates to a force.
    public bool CanPressStop    => Busy is VmBusy.None or VmBusy.Stopping;

    public bool IsStopForcing => Busy == VmBusy.Stopping;
    public string StopLabel => IsStopForcing ? "Force Stop" : "Stop";

    public string StateGlyph => State switch
    {
        VmState.Running => "▶",
        VmState.Stopped => "■",
        VmState.Paused  => "⏸",
        _               => "●"
    };

    public string StateLabel => State switch
    {
        VmState.Running => "Running",
        VmState.Stopped => "Stopped",
        VmState.Paused  => "Paused",
        _               => "Unknown"
    };

    public string StateColor => State switch
    {
        VmState.Running => "#5BD6A0",
        VmState.Paused  => "#E0B050",
        _               => "#8FA6BC"
    };
}
