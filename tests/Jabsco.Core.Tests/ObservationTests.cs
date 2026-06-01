using Jabsco.Core.Persistence.Profiles;
using Jabsco.Core.Sessions;
using Jabsco.Core.VmHost;

namespace Jabsco.Core.Tests;

// The text the model sees with no screen.
public sealed class ObservationTests
{
    [Fact]
    public void Disconnected_ListsProfilesOnly()
    {
        var text = Observation.Build(
            ConnectionKind.Disconnected, hostAddress: null, currentVmId: null,
            profiles: [Rdp("work", "10.0.0.9"), VmHostProfile("lab", "hv-host")],
            vms: []);

        Assert.Contains("disconnected", text);
        Assert.Contains("work", text);
        Assert.Contains("lab", text);
        Assert.DoesNotContain("VMs on this host", text);
    }

    [Fact]
    public void OnHost_ListsVmsWithStateAndId()
    {
        var dc = new VmInfo(Guid.NewGuid(), "DC01", VmState.Running);

        var text = Observation.Build(
            ConnectionKind.VmHost, hostAddress: "hv-host", currentVmId: null,
            profiles: [], vms: [dc]);

        Assert.Contains("hv-host", text);
        Assert.Contains("DC01", text);
        Assert.Contains("Running", text);
        Assert.Contains(dc.Id.ToString(), text);
    }

    [Fact]
    public void MarksCurrentVm()
    {
        var dc = new VmInfo(Guid.NewGuid(), "DC01", VmState.Running);

        var text = Observation.Build(
            ConnectionKind.VmHost, hostAddress: "hv-host", currentVmId: dc.Id,
            profiles: [], vms: [dc]);

        Assert.Contains("(current)", text);
    }

    private static Profile Rdp(string name, string host) => Prof(name, host, "tcp", null);
    private static Profile VmHostProfile(string name, string host) => Prof(name, host, "hvsocket", null);

    private static Profile Prof(string name, string host, string transport, Guid? vmId) => new(
        Id: 1, Name: name, Host: host, Port: 3389, VmId: vmId, Username: null,
        CredentialRef: null, Transport: transport, Resolution: "1280x800", LastModel: null,
        ToolPolicyId: null, CreatedAt: DateTimeOffset.UtcNow, LastUsedAt: DateTimeOffset.UtcNow,
        UseCount: 0);
}
