using Jabsco.Common.Events;

namespace Jabsco.Core.VmHost;

public sealed class HyperVHost : IVmHost
{
    private readonly string _address;
    private readonly string? _username;
    private readonly string? _password;

    public HyperVHost(string address, string? username = null, string? password = null)
    {
        _address  = address;
        _username = username;
        _password = password;
    }

    // Hyper-V RequestStateChange RequestedState codes (root\virtualization\v2). Shutdown is
    // graceful via Msvm_ShutdownComponent, not a state change, so it maps to null.
    public static ushort? RequestedStateFor(VmOperation operation) => operation switch
    {
        VmOperation.Start    => 2,      // Enabled (running)
        VmOperation.Resume   => 2,      // back to Enabled from Paused
        VmOperation.PowerOff => 3,      // Disabled (forced off)
        VmOperation.Pause    => 32768,  // Paused
        VmOperation.Save     => 32769,  // Suspended (saved state)
        VmOperation.Restart  => 11,     // Reset
        VmOperation.Shutdown => null,
        _                    => null
    };

    public Task<IReadOnlyList<VmInfo>> ListVmsAsync(CancellationToken ct = default) =>
        OperatingSystem.IsWindows()
            ? Task.Run(ListVmsWindows, ct)
            : Task.FromResult<IReadOnlyList<VmInfo>>([]);

    public Task ChangeStateAsync(Guid vmId, VmOperation operation, CancellationToken ct = default) =>
        OperatingSystem.IsWindows()
            ? Task.Run(() => ChangeStateWindows(vmId, operation), ct)
            : throw new PlatformNotSupportedException("VM actions require Windows.");

    public Task<Guid> CreateVmAsync(VmSpec spec, CancellationToken ct = default) =>
        OperatingSystem.IsWindows()
            ? Task.Run(() => CreateVmWindows(spec), ct)
            : throw new PlatformNotSupportedException("VM setup requires Windows.");

    public Task AlterVmAsync(Guid vmId, VmSpec spec, CancellationToken ct = default) =>
        OperatingSystem.IsWindows()
            ? Task.Run(() => AlterVmWindows(vmId, spec), ct)
            : throw new PlatformNotSupportedException("VM setup requires Windows.");

    private IReadOnlyList<VmInfo> ListVmsWindows()
    {
        // System.Management is Windows-only; guarded by IsWindows() above.
#pragma warning disable CA1416
        var scope = ConnectScope();

        // No Caption filter — host Name is a hostname, VM Name is a GUID.
        // Guid.TryParse below naturally skips the host row without locale issues.
        var query = new System.Management.ObjectQuery(
            "SELECT Name, ElementName, EnabledState FROM Msvm_ComputerSystem");
        using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();

        var vms = new List<VmInfo>();
        foreach (System.Management.ManagementObject obj in results)
        {
            var rawId = obj["Name"]?.ToString() ?? string.Empty;
            var name  = obj["ElementName"]?.ToString() ?? rawId;
            var state = ParseState(obj["EnabledState"]);

            rawId = rawId.Trim('{', '}');
            if (Guid.TryParse(rawId, out var id))
                vms.Add(new VmInfo(id, name, state));
        }
#pragma warning restore CA1416

        return vms.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static VmState ParseState(object? value)
    {
        if (value == null) return VmState.Other;
        return Convert.ToUInt32(value) switch
        {
            2     => VmState.Running,
            3     => VmState.Stopped,
            32769 => VmState.Paused,
            _     => VmState.Other
        };
    }

    private void ChangeStateWindows(Guid vmId, VmOperation operation)
    {
#pragma warning disable CA1416
        var scope = ConnectScope();
        using var vm = FindVm(scope, vmId)
            ?? throw new InvalidOperationException($"VM {vmId} not found on {_address}.");

        if (operation == VmOperation.Shutdown) { GracefulShutdown(vm); return; }

        var requested = RequestedStateFor(operation)
            ?? throw new InvalidOperationException($"Unsupported VM operation: {operation}.");
        var inParams = vm.GetMethodParameters("RequestStateChange");
        inParams["RequestedState"] = requested;
        using var outParams = vm.InvokeMethod("RequestStateChange", inParams, null);
        ThrowOnFailure(outParams, "RequestStateChange");
#pragma warning restore CA1416
    }

    private static void GracefulShutdown(System.Management.ManagementObject vm)
    {
#pragma warning disable CA1416
        foreach (System.Management.ManagementObject component in vm.GetRelated("Msvm_ShutdownComponent"))
            using (component)
            {
                var inParams = component.GetMethodParameters("InitiateShutdown");
                inParams["Force"] = true;
                inParams["Reason"] = "Requested by Jabsco";
                using var outParams = component.InvokeMethod("InitiateShutdown", inParams, null);
                ThrowOnFailure(outParams, "InitiateShutdown");
                return;
            }
        throw new InvalidOperationException("VM has no shutdown component (integration services off?).");
#pragma warning restore CA1416
    }

    // 0 = done, 4096 = job started asynchronously; anything else is a failure.
    private static void ThrowOnFailure(System.Management.ManagementBaseObject outParams, string method)
    {
#pragma warning disable CA1416
        var code = Convert.ToUInt32(outParams["ReturnValue"]);
        if (code != 0 && code != 4096)
            throw new InvalidOperationException($"{method} failed (code {code}).");
#pragma warning restore CA1416
    }

    private System.Management.ManagementScope ConnectScope()
    {
#pragma warning disable CA1416
        var connOpts = new System.Management.ConnectionOptions();
        if (_username != null) connOpts.Username = _username;
        if (_password != null) connOpts.Password = _password;

        var scope = new System.Management.ManagementScope(
            $@"\\{_address}\root\virtualization\v2", connOpts);
        scope.Connect();
        return scope;
#pragma warning restore CA1416
    }

    // Provisioning runs Hyper-V cmdlets on the host over PowerShell remoting. The script is
    // generated from the spec (pure, below); running it is the Windows-only integration layer.
    private Guid CreateVmWindows(VmSpec spec)
    {
        var (stdout, stderr, exitCode) = RunOnHost(CreateScript(spec));

        // The script prints the id right after New-VM, so it survives a later config failure.
        var firstLine = stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var haveId = Guid.TryParse(firstLine, out var id);

        if (exitCode != 0)
        {
            // A surfaced id means we connected fine and only a config cmdlet failed — no WinRM hint.
            if (haveId)
                throw new InvalidOperationException(
                    $"VM created ({id:D}) but configuration failed: {stderr} " +
                    "The VM exists on the host — alter or delete it.");
            throw ProvisioningFailure(stderr);
        }
        if (haveId) return id;
        throw new InvalidOperationException($"VM created but no id was returned. Output: {stdout}");
    }

    private void AlterVmWindows(Guid vmId, VmSpec spec)
    {
        var (_, stderr, exitCode) = RunOnHost(AlterScript(vmId, spec));
        if (exitCode != 0)
            throw ProvisioningFailure(stderr);
    }

    // Generates the PowerShell that creates a VM and prints its id. Only the provided fields
    // become cmdlets, so the model controls exactly what's configured.
    public static string CreateScript(VmSpec spec)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.Append($"$vm = New-VM -Name {Ps(spec.Name ?? string.Empty)} -NoVHD");
        if (spec.Generation is { } gen) sb.Append($" -Generation {(int)gen}");
        if (spec.MemoryMB is { } mem) sb.Append($" -MemoryStartupBytes {mem}MB");
        sb.AppendLine();
        // Print the id before any config that can fail, so a partial failure still surfaces it.
        sb.AppendLine("$vm.Id.Guid");
        AppendCommonSettings(sb, spec);
        return sb.ToString();
    }

    // Generates the PowerShell that alters an existing VM. Generation can't change after
    // creation, so it's ignored here; everything else maps to a Set-/Enable- cmdlet.
    public static string AlterScript(Guid vmId, VmSpec spec)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine("$ErrorActionPreference = 'Stop'");
        sb.AppendLine($"$vm = Get-VM -Id {Ps(vmId.ToString())}");
        if (spec.Name is { } name) sb.AppendLine($"Rename-VM -VM $vm -NewName {Ps(name)}");
        if (spec.MemoryMB is { } mem) sb.AppendLine($"Set-VMMemory -VM $vm -StartupBytes {mem}MB");
        AppendCommonSettings(sb, spec);
        return sb.ToString();
    }

    private static void AppendCommonSettings(System.Text.StringBuilder sb, VmSpec spec)
    {
        if (spec.ProcessorCount is { } cpu) sb.AppendLine($"Set-VMProcessor -VM $vm -Count {cpu}");
        if (spec.VhdSizeGB is { } gb)
        {
            sb.AppendLine("$vhd = Join-Path (Get-VMHost).VirtualHardDiskPath ($vm.Name + '.vhdx')");
            sb.AppendLine($"New-VHD -Path $vhd -SizeBytes {gb}GB -Dynamic | Out-Null");
            sb.AppendLine("Add-VMHardDiskDrive -VM $vm -Path $vhd");
        }
        if (spec.IsoPath is { } iso) {
            sb.AppendLine($"$DVDDrive = Add-VMDvdDrive -VM $vm -Path {Ps(iso)}");
            sb.AppendLine("$BootOrder = (Get-VMFirmware -VM $vm).BootOrder");
            sb.AppendLine("$NewBootOrder = @($DVDDrive) + ($BootOrder | Where-Object { $_.Device -ne $DVDDrive.Device })");
            sb.AppendLine("Set-VMFirmware -VM $vm -BootOrder $NewBootOrder");
        }
        if (spec.SecureBoot is { } secure)
            sb.AppendLine($"Set-VMFirmware -VM $vm -EnableSecureBoot {(secure ? "On" : "Off")}");
        if (spec.Tpm is true)
        {
            sb.AppendLine("Set-VMKeyProtector -VM $vm -NewLocalKeyProtector");
            sb.AppendLine("Enable-VMTPM -VM $vm");
        }
        else if (spec.Tpm is false) sb.AppendLine("Disable-VMTPM -VM $vm");
        if (spec.NetworkAdapter is { } nic)
            sb.AppendLine($"Get-VMNetworkAdapter -VM $vm | Connect-VMNetworkAdapter -SwitchName {Ps(nic)}");
        if (spec.Checkpoints is { } cp)
            sb.AppendLine($"Set-VM -VM $vm -CheckpointType {(cp ? "Production" : "Disabled")}");
        if (spec.GuestServices is { } gs)
            sb.AppendLine($"{(gs ? "Enable" : "Disable")}-VMIntegrationService -VM $vm -Name 'Guest Service Interface'");
        if (spec.EnhancedSession is { } es)
            sb.AppendLine($"Set-VMHost -EnableEnhancedSessionMode ${es.ToString().ToLowerInvariant()}");
    }

    // Single-quoted PowerShell literal; embedded quotes are doubled.
    private static string Ps(string value) => "'" + value.Replace("'", "''") + "'";

    // Runs a provisioning script against the host, passing host/creds/script through the
    // environment so no secret hits the command line. The script runs on the host via WinRM
    // (see RemoteWrapper). Returns the raw result; callers decide how to interpret a non-zero
    // exit (create surfaces the VM id from stdout even on a config failure).
    private (string Stdout, string Stderr, int ExitCode) RunOnHost(string hostScript)
    {
        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName               = "powershell.exe",
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false,
            CreateNoWindow         = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add("-");
        psi.Environment["JABSCO_HV_HOST"]   = _address;
        psi.Environment["JABSCO_HV_SCRIPT"] = hostScript;
        if (_username != null) psi.Environment["JABSCO_HV_USER"] = _username;
        if (_password != null) psi.Environment["JABSCO_HV_PASS"] = _password;

        using var proc = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Could not start powershell.exe.");
        proc.StandardInput.Write(RemoteWrapper(_username != null));
        proc.StandardInput.Close();
        // Drain both pipes concurrently. Reading one to the end before the other deadlocks
        // when the child fills the other pipe's buffer (PowerShell errors flood stderr).
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();
        var stderrTask = proc.StandardError.ReadToEndAsync();
        proc.WaitForExit();
        var stdout = stdoutTask.GetAwaiter().GetResult();
        var stderr = stderrTask.GetAwaiter().GetResult();
        return (stdout.Trim(), stderr.Trim(), proc.ExitCode);
    }

    // Runs the provisioning script on the host via WinRM (Invoke-Command), so New-VM/New-VHD
    // execute where Hyper-V lives — a DCOM CIM session can't carry those write operations
    // reliably (it half-creates VMs, then RPC-times-out). WinRM client setup (service running +
    // host in TrustedHosts) needs admin, so we don't self-configure it; ProvisioningFailure
    // surfaces the one-time command. The host side needs Enable-PSRemoting.
    private static string RemoteWrapper(bool withCredential)
    {
        var prologue = "$ErrorActionPreference = 'Stop'\n"
            + "$sb = [scriptblock]::Create($env:JABSCO_HV_SCRIPT)\n";

        if (!withCredential)
            return prologue
                + "Invoke-Command -ComputerName $env:JABSCO_HV_HOST -ScriptBlock $sb\n";

        return prologue
            + "$sec = ConvertTo-SecureString $env:JABSCO_HV_PASS -AsPlainText -Force\n"
            + "$cred = New-Object System.Management.Automation.PSCredential($env:JABSCO_HV_USER, $sec)\n"
            + "Invoke-Command -ComputerName $env:JABSCO_HV_HOST -Credential $cred -Authentication Negotiate -ScriptBlock $sb\n";
    }

    // WinRM client setup (service + TrustedHosts) requires elevation we don't have, so a
    // connection-level failure points at the one-time command to run as admin on this machine.
    private InvalidOperationException ProvisioningFailure(string stderr)
    {
        var msg = $"Hyper-V provisioning failed: {stderr}";
        if (stderr.Contains("WinRM", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("TrustedHosts", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("WS-Management", StringComparison.OrdinalIgnoreCase)
            || stderr.Contains("cannot process the request", StringComparison.OrdinalIgnoreCase))
            msg += " Run once in an elevated PowerShell on this machine: "
                + "Set-Service WinRM -StartupType Automatic; Start-Service WinRM; "
                + $"Set-Item WSMan:\\localhost\\Client\\TrustedHosts -Value '{_address}' -Concatenate -Force "
                + "(and Enable-PSRemoting -Force on the host).";
        return new InvalidOperationException(msg);
    }

    private static System.Management.ManagementObject? FindVm(System.Management.ManagementScope scope, Guid vmId)
    {
#pragma warning disable CA1416
        var query = new System.Management.ObjectQuery(
            $"SELECT * FROM Msvm_ComputerSystem WHERE Name='{vmId}'");
        using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
        foreach (System.Management.ManagementObject vm in searcher.Get())
            return vm;
        return null;
#pragma warning restore CA1416
    }
}
