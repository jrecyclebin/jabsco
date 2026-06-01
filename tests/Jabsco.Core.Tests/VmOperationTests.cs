using Jabsco.Common.Events;
using Jabsco.Core.VmHost;

namespace Jabsco.Core.Tests;

// The model-facing operation tokens and their Hyper-V RequestStateChange codes. The WMI
// invocation itself is Windows-only integration; these mappings are the testable core.
public sealed class VmOperationTests
{
    [Theory]
    [InlineData("start", VmOperation.Start)]
    [InlineData("shutdown", VmOperation.Shutdown)]
    [InlineData("poweroff", VmOperation.PowerOff)]
    [InlineData("save", VmOperation.Save)]
    [InlineData("pause", VmOperation.Pause)]
    [InlineData("resume", VmOperation.Resume)]
    [InlineData("restart", VmOperation.Restart)]
    [InlineData("START", VmOperation.Start)]
    public void Parse_MapsKnownTokens(string token, VmOperation expected)
    {
        Assert.Equal(expected, VmOperations.Parse(token));
    }

    [Theory]
    [InlineData("reboot")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_ReturnsNullForUnknown(string? token)
    {
        Assert.Null(VmOperations.Parse(token));
    }

    [Fact]
    public void ToToken_RoundTripsThroughParse()
    {
        foreach (VmOperation op in Enum.GetValues<VmOperation>())
            Assert.Equal(op, VmOperations.Parse(op.ToToken()));
    }

    [Theory]
    [InlineData(VmOperation.Start, (ushort)2)]
    [InlineData(VmOperation.Resume, (ushort)2)]
    [InlineData(VmOperation.PowerOff, (ushort)3)]
    [InlineData(VmOperation.Pause, (ushort)32768)]
    [InlineData(VmOperation.Save, (ushort)32769)]
    [InlineData(VmOperation.Restart, (ushort)11)]
    public void RequestedStateFor_MapsPowerOperations(VmOperation op, ushort expected)
    {
        Assert.Equal(expected, HyperVHost.RequestedStateFor(op));
    }

    [Fact]
    public void RequestedStateFor_ShutdownIsGraceful_NotAStateChange()
    {
        Assert.Null(HyperVHost.RequestedStateFor(VmOperation.Shutdown));
    }
}
