using Jabsco.Core.VmHost;

namespace Jabsco.Ui.ViewModels;

public sealed class VmViewModel
{
    public Guid Id { get; }
    public string Name { get; }
    public bool CanConnect { get; }
    public string DisplayText { get; }
    public double ItemOpacity { get; }

    public VmViewModel(VmInfo vm)
    {
        Id = vm.Id;
        Name = vm.Name;
        CanConnect = vm.State is VmState.Running or VmState.Paused;
        DisplayText = vm.State switch
        {
            VmState.Running => $"▶  {vm.Name}",
            VmState.Stopped => $"■  {vm.Name}",
            VmState.Paused  => $"⏸  {vm.Name}",
            _               => vm.Name
        };
        ItemOpacity = CanConnect ? 1.0 : 0.45;
    }
}
