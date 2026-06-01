using Jabsco.Common.Events;
using Jabsco.Core.VmHost;

namespace Jabsco.Core.Tests;

// The PowerShell that vm_setup runs on the host is generated from a VmSpec. Generation is the
// testable core; running it over WinRM is the Windows-only integration layer. Create emits
// New-VM; alter targets an existing VM by id and touches only the provided fields.
public sealed class VmProvisioningScriptTests
{
    [Fact]
    public void Create_StartsWithNewVm_NameAndGeneration()
    {
        var script = HyperVHost.CreateScript(new VmSpec(Name: "build01", Generation: VmGeneration.Gen2));

        Assert.Contains("New-VM", script);
        Assert.Contains("'build01'", script);
        Assert.Contains("-Generation 2", script);
        // Returns the new VM's id for the caller to report.
        Assert.Contains(".Id.Guid", script);
    }

    [Fact]
    public void Create_PrintsIdBeforeConfig_SoPartialFailureStillSurfacesIt()
    {
        var script = HyperVHost.CreateScript(new VmSpec(
            Name: "build01", Generation: VmGeneration.Gen2, VhdSizeGB: 60, ProcessorCount: 4));

        // A config cmdlet (New-VHD, Set-VMProcessor) can fail; the id must already be printed.
        Assert.True(script.IndexOf(".Id.Guid", StringComparison.Ordinal)
                  < script.IndexOf("New-VHD", StringComparison.Ordinal));
        Assert.True(script.IndexOf(".Id.Guid", StringComparison.Ordinal)
                  < script.IndexOf("Set-VMProcessor", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_Minimal_OmitsOptionalCmdlets()
    {
        var script = HyperVHost.CreateScript(new VmSpec(Name: "build01", Generation: VmGeneration.Gen2));

        Assert.DoesNotContain("Set-VMProcessor", script);
        Assert.DoesNotContain("New-VHD", script);
        Assert.DoesNotContain("Add-VMDvdDrive", script);
        Assert.DoesNotContain("Enable-VMTPM", script);
    }

    [Fact]
    public void Create_Full_EmitsEveryCmdlet()
    {
        var script = HyperVHost.CreateScript(new VmSpec(
            Name: "build01", Generation: VmGeneration.Gen2, MemoryMB: 4096, VhdSizeGB: 60,
            IsoPath: @"C:\iso\win.iso", Tpm: true, SecureBoot: true, ProcessorCount: 4,
            NetworkAdapter: "Default Switch", Checkpoints: true, GuestServices: true, EnhancedSession: true));

        Assert.Contains("-MemoryStartupBytes 4096MB", script);
        Assert.Contains("New-VHD", script);
        Assert.Contains("60GB", script);
        Assert.Contains("Add-VMHardDiskDrive", script);
        Assert.Contains("Add-VMDvdDrive", script);
        Assert.Contains(@"'C:\iso\win.iso'", script);
        Assert.Contains("Enable-VMTPM", script);
        Assert.Contains("-EnableSecureBoot On", script);
        Assert.Contains("Set-VMProcessor", script);
        Assert.Contains("-Count 4", script);
        Assert.Contains("'Default Switch'", script);
        Assert.Contains("Enable-VMIntegrationService", script);
    }

    [Fact]
    public void Alter_TargetsExistingVm_NoNewVm()
    {
        var vm = Guid.NewGuid();
        var script = HyperVHost.AlterScript(vm, new VmSpec(MemoryMB: 8192));

        Assert.Contains("Get-VM", script);
        Assert.Contains(vm.ToString(), script);
        Assert.DoesNotContain("New-VM", script);
        Assert.Contains("Set-VMMemory", script);
        Assert.Contains("8192MB", script);
    }

    [Fact]
    public void Alter_OmittedFields_ProduceNoCmdlets()
    {
        var script = HyperVHost.AlterScript(Guid.NewGuid(), new VmSpec(MemoryMB: 8192));

        Assert.DoesNotContain("Set-VMProcessor", script);
        Assert.DoesNotContain("New-VHD", script);
        Assert.DoesNotContain("Set-VMFirmware", script);
        Assert.DoesNotContain("Rename-VM", script);
    }

    [Fact]
    public void SingleQuotesInValues_AreEscaped()
    {
        var script = HyperVHost.CreateScript(new VmSpec(Name: "o'brien", Generation: VmGeneration.Gen1));

        Assert.Contains("'o''brien'", script);
    }

    [Fact]
    public void SecureBootOff_EmitsOff()
    {
        var script = HyperVHost.AlterScript(Guid.NewGuid(), new VmSpec(SecureBoot: false));

        Assert.Contains("-EnableSecureBoot Off", script);
    }
}
