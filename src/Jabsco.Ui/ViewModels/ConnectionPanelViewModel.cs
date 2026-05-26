using System.Collections.ObjectModel;
using System.Net;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jabsco.Core.Config;
using Jabsco.Core.Credentials;
using Jabsco.Core.HyperV;
using Jabsco.Core.Persistence;
using Jabsco.Core.Persistence.Profiles;
using Jabsco.Core.Rdp;
using Microsoft.Extensions.Logging;

namespace Jabsco.Ui.ViewModels;

public partial class ConnectionPanelViewModel : ViewModelBase
{
    private readonly MainWindowViewModel _main;
    private readonly ILoggerFactory _loggerFactory;
    private JabscoDb? _db;
    private ICredentialStore? _credentials;
    private int? _editingProfileId;

    public ObservableCollection<RecentConnectionViewModel> RecentConnections { get; } = [];
    public ObservableCollection<HyperVVmViewModel> HyperVVms { get; } = [];

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
    [NotifyCanExecuteChangedFor(nameof(ConnectNewCommand))]
    private bool _isHyperVMode;

    [ObservableProperty]
    private string _hyperVHost = "localhost";

    [ObservableProperty]
    private string _hyperVUsername = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPassword))]
    private string _hyperVPassword = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectNewCommand))]
    [NotifyPropertyChangedFor(nameof(IsVmIdEditable))]
    private HyperVVmViewModel? _selectedVm;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VmListPlaceholderText))]
    private bool _isLoadingVms;

    // Editable only when the "Enter GUID manually" sentinel is selected.
    public bool IsVmIdEditable => SelectedVm?.IsManualEntry == true;

    public string VmListPlaceholderText => IsLoadingVms ? "Loading…" : "Select a VM";

    // Shows the selected VM's GUID, or the user-typed value when manual entry is active.
    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectNewCommand))]
    private string _vmId = "";

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

    partial void OnIsHyperVModeChanged(bool value)
    {
        OnPropertyChanged(nameof(HasPassword));
        if (value) _ = RefreshVmsAsync();
    }

    partial void OnSelectedVmChanged(HyperVVmViewModel? value)
    {
        if (value is { IsManualEntry: false, Id: { } id })
            VmId = id.ToString();
        else if (value is null)
            VmId = "";
        // When switching to manual entry, preserve whatever was in VmId.
    }

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
            // No stored password — pre-fill the form so the user can enter one
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

    [RelayCommand]
    private Task RefreshVms() => RefreshVmsAsync();

    [RelayCommand(CanExecute = nameof(CanConnectNew))]
    private Task ConnectNew() => IsHyperVMode
        ? ConnectVmAsync(ResolveVmId()!.Value, HyperVHost)
        : ConnectToAsync(NewHost.Trim(), NewUsername.Trim().NullIfEmpty(), NewPassword.NullIfEmpty());

    private bool CanConnectNew()
    {
        if (IsConnecting) return false;
        if (!IsHyperVMode) return !string.IsNullOrWhiteSpace(NewHost);

        if (SelectedVm is { IsManualEntry: false, CanConnect: true }) return true;
        if (SelectedVm?.IsManualEntry == true) return Guid.TryParse(VmId.Trim(), out _);
        return false;
    }

    private Guid? ResolveVmId()
    {
        if (SelectedVm is { IsManualEntry: false, Id: { } id }) return id;
        if (Guid.TryParse(VmId.Trim(), out var parsed)) return parsed;
        return null;
    }

    private async Task RefreshVmsAsync()
    {
        IsLoadingVms = true;
        ErrorMessage = null;
        try
        {
            var vms = await HyperVService.ListVmsAsync(HyperVHost);
            HyperVVms.Clear();
            foreach (var vm in vms)
                HyperVVms.Add(new HyperVVmViewModel(vm));
            HyperVVms.Add(HyperVVmViewModel.ManualEntry);

            SelectedVm = HyperVVms.FirstOrDefault(v => v.CanConnect)
                         ?? HyperVVmViewModel.ManualEntry;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Could not list VMs: {ex.Message}";
            HyperVVms.Clear();
            HyperVVms.Add(HyperVVmViewModel.ManualEntry);
            SelectedVm = HyperVVmViewModel.ManualEntry;
        }
        finally
        {
            IsLoadingVms = false;
        }
    }

    private async Task ConnectVmAsync(Guid vmId, string host)
    {
        IsConnecting = true;
        ErrorMessage = null;
        try
        {
            var client = new FreeRdpClient(_loggerFactory.CreateLogger<FreeRdpClient>());
            var options = new ConnectOptions(
                Host: host,
                Username: HyperVUsername.NullIfEmpty(),
                Password: HyperVPassword.NullIfEmpty(),
                VmId: vmId,
                Transport: TransportKind.HvSocket,
                AcceptAnyCertificate: true);

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            await client.ConnectAsync(options, cts.Token);

            var displayName = SelectedVm is { IsManualEntry: false } vm ? vm.Name : vmId.ToString();
            var session = new SessionViewModel(client, displayName, _loggerFactory);
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
        // Update an existing profile when in edit mode
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

        // Find-or-insert
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
