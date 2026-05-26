namespace Jabsco.Core.HyperV;

public static class HyperVService
{
    /// <summary>
    /// Returns all Hyper-V VMs on the given host.
    /// Returns an empty list on non-Windows platforms or if Hyper-V is unavailable.
    /// </summary>
    public static Task<IReadOnlyList<HyperVVm>> ListVmsAsync(string host = "localhost") =>
        OperatingSystem.IsWindows()
            ? Task.Run(() => ListVmsWindows(host))
            : Task.FromResult<IReadOnlyList<HyperVVm>>([]);

    private static IReadOnlyList<HyperVVm> ListVmsWindows(string host)
    {
        // System.Management is Windows-only; guard ensures this path is never reached
        // on other platforms, so the pragma suppresses the CA1416 platform warning.
#pragma warning disable CA1416
        var scope = new System.Management.ManagementScope(
            $@"\\{host}\root\virtualization\v2");
        scope.Connect();

        var query = new System.Management.ObjectQuery(
            "SELECT Name, ElementName, EnabledState FROM Msvm_ComputerSystem " +
            "WHERE Caption = 'Virtual Machine'");
        using var searcher = new System.Management.ManagementObjectSearcher(scope, query);
        using var results = searcher.Get();

        var vms = new List<HyperVVm>();
        foreach (System.Management.ManagementObject obj in results)
        {
            var rawId   = obj["Name"]?.ToString() ?? string.Empty;
            var name    = obj["ElementName"]?.ToString() ?? rawId;
            var state   = ParseState(obj["EnabledState"]);

            // WMI Name may include braces: {GUID} — strip them
            rawId = rawId.Trim('{', '}');
            if (Guid.TryParse(rawId, out var id))
                vms.Add(new HyperVVm(id, name, state));
        }
#pragma warning restore CA1416

        return vms.OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static HyperVVmState ParseState(object? value)
    {
        if (value == null) return HyperVVmState.Other;
        return Convert.ToUInt32(value) switch
        {
            2     => HyperVVmState.Running,
            3     => HyperVVmState.Stopped,
            32769 => HyperVVmState.Paused,
            _     => HyperVVmState.Other
        };
    }
}
