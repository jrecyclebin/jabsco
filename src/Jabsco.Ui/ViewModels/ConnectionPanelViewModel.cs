using System.Collections.ObjectModel;
using System.Net;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jabsco.Core.Config;
using Jabsco.Core.Credentials;
using Jabsco.Core.Persistence;
using Jabsco.Core.Persistence.Profiles;
using Jabsco.Core.Rdp;
using Jabsco.Core.Sessions;
using Jabsco.Core.VmHost;
using Microsoft.Extensions.Logging;

namespace Jabsco.Ui.ViewModels;

public partial class ConnectionPanelViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger<ConnectionPanelViewModel> _log;
    private JabscoDb? _db;
    private ICredentialStore? _credentials;
    private int? _editingProfileId;
    private IVmHost? _vmHost;

    public ObservableCollection<RecentConnectionViewModel> RecentConnections { get; } = [];
    public ObservableCollection<VmViewModel> Vms { get; } = [];

    public bool HasRecentConnections => RecentConnections.Count > 0;
    public bool IsEditingProfile => _editingProfileId.HasValue;
    public string FormTitle => _editingProfileId.HasValue
        ? (_passwordPromptMode ? "ENTER PASSWORD" : "EDIT CONNECTION")
        : "NEW SESSION";

    private bool _passwordPromptMode;

    private void SetEditingProfile(int? id, bool passwordPrompt = false)
    {
        _editingProfileId = id;
        _passwordPromptMode = passwordPrompt;
        OnPropertyChanged(nameof(IsEditingProfile));
        OnPropertyChanged(nameof(FormTitle));
    }

    // ── RDP form ──────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectNewCommand))]
    private string _newHost = "";

    [ObservableProperty]
    private string _newUsername = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPassword))]
    private string _newPassword = "";

    // ── Hyper-V form ──────────────────────────────────────────────────────────

    [ObservableProperty]
    private bool _isHyperVMode;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectToHyperVHostCommand))]
    private string _hyperVHost = "localhost";

    [ObservableProperty]
    private string _hyperVVmGuid = "";

    [ObservableProperty]
    private string _hyperVUsername = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPassword))]
    private string _hyperVPassword = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HyperVConnectButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectToHyperVHostCommand))]
    private bool _isLoadingVms;

    // True once the host has been queried and the VM list is showing.
    [ObservableProperty]
    private bool _isVmHostConnected;

    public string HyperVConnectButtonText => IsLoadingVms ? "Connecting…" : "Connect";

    // ── Shared ────────────────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectNewCommand))]
    private bool _isConnecting;

    public string ConnectButtonText => IsConnecting ? "Connecting…" : "Connect";

    [ObservableProperty]
    private string? _errorMessage;

    [ObservableProperty]
    private bool _saveCredentials;

    public bool HasPassword => !string.IsNullOrEmpty(IsHyperVMode ? HyperVPassword : NewPassword);

    public bool IsHyperVEnabled { get; }

    public ConnectionPanelViewModel(MainWindowViewModel main, FeatureFlags features)
    {
        _main = main;
        IsHyperVEnabled = features.HyperV;
        _loggerFactory = LoggerFactory.Create(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));
        _log = _loggerFactory.CreateLogger<ConnectionPanelViewModel>();
        RecentConnections.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasRecentConnections));
    }

    public async Task LoadAsync(JabscoDb db, ICredentialStore credentials)
    {
        _db = db;
        _credentials = credentials;

        var profiles = await db.Profiles.GetAllAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            RecentConnections.Clear();
            foreach (var p in profiles)
            {
                RecentConnections.Add(new RecentConnectionViewModel
                {
                    ProfileId = p.Id,
                    Name = p.Name ?? p.Host,
                    Host = p.Host,
                    Port = p.Port,
                    Username = p.Username,
                    VmId = p.VmId,
                    Transport = p.Transport,
                    LastUsed = p.LastUsedAt
                });
            }
        });
    }

    partial void OnIsHyperVModeChanged(bool value) =>
        OnPropertyChanged(nameof(HasPassword));

    [RelayCommand] private void SwitchToRdp()    => IsHyperVMode = false;
    [RelayCommand] private void SwitchToHyperV() => IsHyperVMode = true;

    [RelayCommand]
    private void BeginEdit(RecentConnectionViewModel item)
    {
        ErrorMessage = null;
        if (item.IsHyperV)
        {
            IsHyperVMode = true;
            HyperVHost = item.Host;
            HyperVUsername = item.Username ?? "";
            HyperVPassword = "";
            HyperVVmGuid = item.VmId?.ToString("D") ?? "";
        }
        else
        {
            IsHyperVMode = false;
            NewHost = item.Host;
            NewUsername = item.Username ?? "";
            NewPassword = "";
        }
        SetEditingProfile(item.ProfileId);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        NewHost = "";
        NewUsername = "";
        NewPassword = "";
        HyperVVmGuid = "";
        HyperVPassword = "";
        ErrorMessage = null;
        SetEditingProfile(null);
    }

    [RelayCommand]
    private void BeginDeleteProfile(RecentConnectionViewModel item)
    {
        foreach (var r in RecentConnections) r.IsPendingDelete = false;
        item.IsPendingDelete = true;
    }

    [RelayCommand]
    private void CancelDeleteProfile(RecentConnectionViewModel item) => item.IsPendingDelete = false;

    [RelayCommand]
    private async Task ConfirmDeleteProfile(RecentConnectionViewModel item)
    {
        if (_db == null) return;
        if (_editingProfileId == item.ProfileId) CancelEdit();
        await _db.Profiles.DeleteAsync(item.ProfileId);
        RecentConnections.Remove(item);
    }

    [RelayCommand]
    private async Task Connect(RecentConnectionViewModel recent)
    {
        var profile = _db != null ? await _db.Profiles.GetByIdAsync(recent.ProfileId) : null;
        string? password = profile != null ? await GetPasswordForProfile(profile) : null;
        bool isHyperV = profile != null
            && string.Equals(profile.Transport, "hvsocket", StringComparison.OrdinalIgnoreCase);

        // RDP needs a password; if none is saved, prompt for it. Hyper-V uses the host creds
        // (which may legitimately be empty), so don't block on a missing password there.
        if (!isHyperV && password == null)
        {
            IsHyperVMode = false;
            NewHost = recent.Host;
            NewUsername = recent.Username ?? "";
            NewPassword = "";
            ErrorMessage = null;
            SetEditingProfile(recent.ProfileId, passwordPrompt: true);
            return;
        }

        if (isHyperV)
        {
            var host = new HostConnection(profile!.Host, profile.Username, password);
            await ConnectHyperVAsync(profile.Name ?? profile.Host, host, profile.VmId);
            return;
        }

        await ConnectRdpAsync(recent.Host, new ConnectOptions(
            Host: recent.Host,
            Username: recent.Username,
            Password: password,
            AcceptAnyCertificate: true));
    }

    // ── RDP connect ───────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConnectNew))]
    private Task ConnectNew() =>
        ConnectRdpAsync(NewHost.Trim(), new ConnectOptions(
            Host: NewHost.Trim(),
            Username: NewUsername.Trim().NullIfEmpty(),
            Password: NewPassword.NullIfEmpty(),
            AcceptAnyCertificate: true));

    private bool CanConnectNew() => !IsConnecting && !string.IsNullOrWhiteSpace(NewHost);

    // ── Hyper-V phase 1: connect to host ─────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConnectToHyperVHost))]
    private Task ConnectToHyperVHost() => ConnectHyperVHostAsync();

    private bool CanConnectToHyperVHost() => !IsLoadingVms && !string.IsNullOrWhiteSpace(HyperVHost);

    // ── Hyper-V phase 2: change host, connect VM, host management ────────────

    [RelayCommand]
    private void ChangeHyperVHost()
    {
        IsVmHostConnected = false;
        Vms.Clear();
        _vmHost = null;
        ErrorMessage = null;
    }

    [RelayCommand]
    private Task RefreshVms() => RefreshVmsAsync();

    [RelayCommand(CanExecute = nameof(CanConnectVm))]
    private Task ConnectVm(VmViewModel vm) =>
        ConnectHyperVAsync(vm.Name, CurrentHostConnection(), vm.Id);

    private bool CanConnectVm(VmViewModel? vm) => vm?.CanConnect == true && !IsConnecting;

    // Launch a host-management session (no VM): the main view shows the VM list and the
    // model gets the vm tools, with computer tools unlocking once it switches to a VM.
    [RelayCommand(CanExecute = nameof(CanConnectToHyperVHost))]
    private Task ConnectToHostDirectly() =>
        ConnectHyperVAsync(HyperVHost.Trim(), CurrentHostConnection(), vmId: null);

    private HostConnection CurrentHostConnection() =>
        new(HyperVHost.Trim(), HyperVUsername.NullIfEmpty(), HyperVPassword.NullIfEmpty());

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task ConnectHyperVHostAsync()
    {
        if (Guid.TryParse(HyperVVmGuid.Trim(), out var guid))
        {
            await ConnectHyperVAsync(guid.ToString("D"), CurrentHostConnection(), guid);
            return;
        }

        IsLoadingVms = true;
        ErrorMessage = null;
        try
        {
            _vmHost = new HyperVHost(HyperVHost.Trim(), HyperVUsername.NullIfEmpty(), HyperVPassword.NullIfEmpty());
            var vms = await _vmHost.ListVmsAsync();
            Vms.Clear();
            foreach (var vm in vms)
                Vms.Add(new VmViewModel(vm));
            IsVmHostConnected = true;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Hyper-V host query for {Host} failed", HyperVHost.Trim());
            ErrorMessage = DescribeHostError(ex);
            _vmHost = null;
        }
        finally
        {
            IsLoadingVms = false;
        }
    }

    // WMI access-denied on the host usually means remote management isn't permitted yet.
    private static string DescribeHostError(Exception ex) => IsAccessDenied(ex)
        ? "Access denied connecting to the Hyper-V host. On the host, run "
          + "'Enable-PSRemoting -Force' in an elevated PowerShell to allow remote management, then retry."
        : $"Could not connect to Hyper-V host: {ex.Message}";

    private static bool IsAccessDenied(Exception ex)
    {
        for (Exception? e = ex; e is not null; e = e.InnerException)
        {
            if (e is UnauthorizedAccessException) return true;
            if (unchecked((uint)e.HResult) == 0x80070005) return true; // E_ACCESSDENIED
            if (e.Message.Contains("access is denied", StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private ConnectionController BuildController() => new(
        new RdpConnector(_loggerFactory),
        _db != null ? new ProfileDirectory(_db.Profiles) : new EmptyProfileDirectory(),
        profile => GetPasswordForProfile(profile),
        host => new HyperVHost(host.Address, host.Username, host.Password));

    private async Task ConnectRdpAsync(string sessionName, ConnectOptions options)
    {
        IsConnecting = true;
        ErrorMessage = null;
        try
        {
            var controller = BuildController();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await controller.StartRdpAsync(options, cts.Token);

            int? profileId = _db != null
                ? await SaveProfileAsync(options.Host, options.Port, options.Username, options.Password, vmId: null, "tcp", profileName: null)
                : null;
            await LaunchSessionAsync(controller, sessionName, profileId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "RDP connect to {Host} failed", options.Host);
            ErrorMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    // vmId null launches a host-management session (host view, no screen).
    private async Task ConnectHyperVAsync(string sessionName, HostConnection host, Guid? vmId)
    {
        IsConnecting = true;
        ErrorMessage = null;
        try
        {
            var controller = BuildController();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await controller.StartHostAsync(host, vmId, cts.Token);

            int? profileId = _db != null
                ? await SaveProfileAsync(host.Address, 2179, host.Username, host.Password, vmId, "hvsocket", sessionName)
                : null;
            await LaunchSessionAsync(controller, sessionName, profileId);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Hyper-V connect to {Host} (vm {Vm}) failed", host.Address, vmId);
            // A host-management connect (no VM) that's denied is almost always remote-management
            // not being enabled; a VM connect failure is a different (2179/RDP) problem.
            ErrorMessage = vmId is null ? DescribeHostError(ex) : $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task LaunchSessionAsync(ConnectionController controller, string sessionName, int? profileId)
    {
        SetEditingProfile(null);
        var session = new SessionViewModel(controller, sessionName, _loggerFactory, _db, profileId);
        _main.AddSession(new SessionTabViewModel(session));
        session.StartLiveView();

        NewHost = "";
        NewUsername = "";
        NewPassword = "";
        SaveCredentials = false;
        await Task.CompletedTask;
    }

    private async Task RefreshVmsAsync()
    {
        if (_vmHost == null) return;
        IsLoadingVms = true;
        ErrorMessage = null;
        try
        {
            var vms = await _vmHost.ListVmsAsync();
            Vms.Clear();
            foreach (var vm in vms)
                Vms.Add(new VmViewModel(vm));
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not refresh VM list: {ex.Message}";
        }
        finally
        {
            IsLoadingVms = false;
        }
    }

    private async Task<string?> GetPasswordForProfile(Profile profile)
    {
        if (profile?.CredentialRef != null && _credentials != null)
        {
            var cred = await _credentials.GetAsync(profile.CredentialRef, default);
            return cred?.Password;
        }
        return null;
    }

    private async Task<int> SaveProfileAsync(string host, int port, string? username, string? password, Guid? vmId, string transport, string? profileName)
    {
        string computedCredRef = $"jabsco:rdp:{host}:{username}" + (port != 3389 ? $":{port}" : "");
        if (_editingProfileId.HasValue)
        {
            var existing = await _db!.Profiles.GetByIdAsync(_editingProfileId.Value);
            if (existing != null)
            {
                var credRef = existing.CredentialRef;
                if (SaveCredentials && password != null && username != null && _credentials != null)
                {
                    credRef = computedCredRef;
                    await _credentials.SetAsync(credRef, new NetworkCredential(username, password), default);
                }
                await _db.Profiles.UpdateAsync(existing with { Host = host, Username = username, VmId = vmId, Name = profileName, CredentialRef = credRef, Transport = transport });
                await _db.Profiles.RecordUsageAsync(_editingProfileId.Value);
                await LoadAsync(_db!, _credentials!);
                return _editingProfileId.Value;
            }
        }

        var match = await _db!.Profiles.FindAsync(host, port, username, vmId);
        if (match != null)
        {
            await _db.Profiles.RecordUsageAsync(match.Id);
            await LoadAsync(_db!, _credentials!);
            return match.Id;
        }

        string? cref = null;
        if (SaveCredentials && password != null && username != null && _credentials != null)
        {
            cref = computedCredRef;
            await _credentials.SetAsync(cref, new NetworkCredential(username, password), default);
        }
        var now = DateTimeOffset.UtcNow;
        var saved = await _db.Profiles.InsertAsync(
            new Profile(0, profileName, host, port, vmId, username, cref, transport, "1280x800", null, null, now, now, 1));
        await LoadAsync(_db!, _credentials!);
        return saved.Id;
    }
}

internal static class StringExtensions
{
    internal static string? NullIfEmpty(this string? s) =>
        string.IsNullOrEmpty(s) ? null : s;
}
