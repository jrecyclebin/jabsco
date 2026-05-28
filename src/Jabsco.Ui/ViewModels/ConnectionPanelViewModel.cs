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
using Jabsco.Core.VmHost;
using Microsoft.Extensions.Logging;

namespace Jabsco.Ui.ViewModels;

public partial class ConnectionPanelViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private readonly ILoggerFactory _loggerFactory;
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
            foreach (var p in profiles.Take(5))
            {
                RecentConnections.Add(new RecentConnectionViewModel
                {
                    ProfileId = p.Id,
                    Name = p.Name ?? p.Host,
                    Host = p.Host,
                    Username = p.Username,
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
        IsHyperVMode = false;
        NewHost = item.Host;
        NewUsername = item.Username ?? "";
        NewPassword = "";
        ErrorMessage = null;
        SetEditingProfile(item.ProfileId);
    }

    [RelayCommand]
    private void CancelEdit()
    {
        NewHost = "";
        NewUsername = "";
        NewPassword = "";
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
        string? password = null;
        if (_credentials != null && _db != null)
        {
            var profile = await _db.Profiles.GetByIdAsync(recent.ProfileId);
            if (profile?.CredentialRef != null)
            {
                var cred = await _credentials.GetAsync(profile.CredentialRef, default);
                password = cred?.Password;
            }
        }

        if (password == null)
        {
            IsHyperVMode = false;
            NewHost = recent.Host;
            NewUsername = recent.Username ?? "";
            NewPassword = "";
            ErrorMessage = null;
            SetEditingProfile(recent.ProfileId, passwordPrompt: true);
            return;
        }

        await ConnectToAsync(recent.Host, recent.Username, password);
    }

    // ── RDP connect ───────────────────────────────────────────────────────────

    [RelayCommand(CanExecute = nameof(CanConnectNew))]
    private Task ConnectNew() =>
        ConnectToAsync(NewHost.Trim(), NewUsername.Trim().NullIfEmpty(), NewPassword.NullIfEmpty());

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
    private Task ConnectVm(VmViewModel vm) => ConnectVmAsync(vm);

    private bool CanConnectVm(VmViewModel? vm) => vm?.CanConnect == true && !IsConnecting;

    [RelayCommand]
    private void ConnectToHostDirectly()
    {
        // Management session support is coming — host tools will live here.
        ErrorMessage = "Management sessions are not yet available. Select a VM to connect.";
    }

    // ── Internal ──────────────────────────────────────────────────────────────

    private async Task ConnectHyperVHostAsync()
    {
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
            ErrorMessage = $"Could not connect to Hyper-V host: {ex.Message}";
            _vmHost = null;
        }
        finally
        {
            IsLoadingVms = false;
        }
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

    private async Task ConnectVmAsync(VmViewModel vm)
    {
        IsConnecting = true;
        ErrorMessage = null;
        try
        {
            var client = new FreeRdpClient(_loggerFactory.CreateLogger<FreeRdpClient>());
            var options = new ConnectOptions(
                Host: HyperVHost.Trim(),
                Username: HyperVUsername.NullIfEmpty(),
                Password: HyperVPassword.NullIfEmpty(),
                VmId: vm.Id,
                Transport: TransportKind.HvSocket,
                AcceptAnyCertificate: true);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await client.ConnectAsync(options, cts.Token);

            var session = new SessionViewModel(client, vm.Name, _loggerFactory);
            _main.AddSession(new SessionTabViewModel(session));
            session.StartLiveView();
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task ConnectToAsync(string host, string? username, string? password)
    {
        IsConnecting = true;
        ErrorMessage = null;
        try
        {
            var client = new FreeRdpClient(_loggerFactory.CreateLogger<FreeRdpClient>());
            var options = new ConnectOptions(
                Host: host,
                Username: username,
                Password: password,
                AcceptAnyCertificate: true);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await client.ConnectAsync(options, cts.Token);

            int? profileId = _db != null ? await SaveProfileAsync(host, username, password) : null;
            SetEditingProfile(null);

            var session = new SessionViewModel(client, host, _loggerFactory, _db, profileId);
            _main.AddSession(new SessionTabViewModel(session));
            session.StartLiveView();

            NewHost = "";
            NewUsername = "";
            NewPassword = "";
            SaveCredentials = false;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Connection failed: {ex.Message}";
        }
        finally
        {
            IsConnecting = false;
        }
    }

    private async Task<int> SaveProfileAsync(string host, string? username, string? password)
    {
        if (_editingProfileId.HasValue)
        {
            var existing = await _db!.Profiles.GetByIdAsync(_editingProfileId.Value);
            if (existing != null)
            {
                var credRef = existing.CredentialRef;
                if (SaveCredentials && password != null && username != null && _credentials != null)
                {
                    credRef = $"jabsco:rdp:{host}:{username}";
                    await _credentials.SetAsync(credRef, new NetworkCredential(username, password), default);
                }
                await _db.Profiles.UpdateAsync(existing with { Host = host, Username = username, CredentialRef = credRef });
                await _db.Profiles.RecordUsageAsync(_editingProfileId.Value);
                await LoadAsync(_db!, _credentials!);
                return _editingProfileId.Value;
            }
        }

        var match = await _db!.Profiles.FindAsync(host, username);
        if (match != null)
        {
            await _db.Profiles.RecordUsageAsync(match.Id);
            await LoadAsync(_db!, _credentials!);
            return match.Id;
        }

        string? cref = null;
        if (SaveCredentials && password != null && username != null && _credentials != null)
        {
            cref = $"jabsco:rdp:{host}:{username}";
            await _credentials.SetAsync(cref, new NetworkCredential(username, password), default);
        }
        var now = DateTimeOffset.UtcNow;
        var saved = await _db.Profiles.InsertAsync(
            new Profile(0, null, host, 3389, username, cref, "tcp", "1280x800", null, null, now, now, 1));
        await LoadAsync(_db!, _credentials!);
        return saved.Id;
    }
}

internal static class StringExtensions
{
    internal static string? NullIfEmpty(this string? s) =>
        string.IsNullOrEmpty(s) ? null : s;
}
