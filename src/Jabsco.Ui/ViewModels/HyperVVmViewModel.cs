using Jabsco.Core.HyperV;

namespace Jabsco.Ui.ViewModels;

public sealed class HyperVVmViewModel
{
    public static readonly HyperVVmViewModel ManualEntry = new();

    public Guid? Id  { get; }
    public string Name { get; }
    public bool CanConnect { get; }
    public bool IsManualEntry { get; }

    public string DisplayText { get; }
    public double ItemOpacity { get; }

    // Sentinel for "Enter GUID manually"
    private HyperVVmViewModel()
    {
        IsManualEntry = true;
        Name = "Enter GUID manually…";
        DisplayText = Name;
        CanConnect = false;
        ItemOpacity = 1.0;
    }

    public HyperVVmViewModel(HyperVVm vm)
    {
        Id = vm.Id;
        Name = vm.Name;
        CanConnect = vm.State is HyperVVmState.Running or HyperVVmState.Paused;
        DisplayText = vm.State switch
        {
            HyperVVmState.Running => $"{vm.Name}  ▶ Running",
            HyperVVmState.Stopped => $"{vm.Name}  ■ Stopped",
            HyperVVmState.Paused  => $"{vm.Name}  ⏸ Paused",
            _                     => vm.Name
        };
        ItemOpacity = CanConnect ? 1.0 : 0.45;
    }
}
