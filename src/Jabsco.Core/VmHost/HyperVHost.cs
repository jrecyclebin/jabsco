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

    public Task<IReadOnlyList<VmInfo>> ListVmsAsync(CancellationToken ct = default) =>
        OperatingSystem.IsWindows()
            ? Task.Run(ListVmsWindows, ct)
            : Task.FromResult<IReadOnlyList<VmInfo>>([]);

    private IReadOnlyList<VmInfo> ListVmsWindows()
    {
        // System.Management is Windows-only; guarded by IsWindows() above.
#pragma warning disable CA1416
        var connOpts = new System.Management.ConnectionOptions();
        if (_username != null) connOpts.Username = _username;
        if (_password != null) connOpts.Password = _password;

        var scope = new System.Management.ManagementScope(
            $@"\\{_address}\root\virtualization\v2", connOpts);
        scope.Connect();

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
}
