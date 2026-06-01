using Jabsco.Common.Events;

namespace Jabsco.Core.Tests;

// The running-VM guard for vm_setup alters. Checkpoints, guest services, and enhanced
// session transport are live-safe; every hardware-class field forces the VM to be stopped.
public sealed class VmSpecTests
{
    [Fact]
    public void RequiresStoppedVm_FalseWhenNothingSet()
    {
        Assert.False(new VmSpec().RequiresStoppedVm);
    }

    [Theory]
    [MemberData(nameof(LiveSafeSpecs))]
    public void RequiresStoppedVm_FalseForLiveSafeFields(VmSpec spec)
    {
        Assert.False(spec.RequiresStoppedVm);
    }

    [Theory]
    [MemberData(nameof(HardwareSpecs))]
    public void RequiresStoppedVm_TrueForHardwareFields(VmSpec spec)
    {
        Assert.True(spec.RequiresStoppedVm);
    }

    public static IEnumerable<object[]> LiveSafeSpecs() =>
    [
        [new VmSpec(Checkpoints: true)],
        [new VmSpec(GuestServices: true)],
        [new VmSpec(EnhancedSession: true)],
        [new VmSpec(Name: "rename", Checkpoints: false, GuestServices: false, EnhancedSession: false)],
    ];

    public static IEnumerable<object[]> HardwareSpecs() =>
    [
        [new VmSpec(Generation: VmGeneration.Gen2)],
        [new VmSpec(MemoryMB: 4096)],
        [new VmSpec(VhdSizeGB: 60)],
        [new VmSpec(IsoPath: @"C:\iso\win.iso")],
        [new VmSpec(Tpm: true)],
        [new VmSpec(SecureBoot: false)],
        [new VmSpec(ProcessorCount: 4)],
        [new VmSpec(NetworkAdapter: "Default Switch")],
    ];
}
