using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Jabsco.Common.Events;
using Jabsco.Core.Agent;
using Jabsco.Core.Approval;
using Jabsco.Core.Config;
using Jabsco.Core.Persistence;
using Jabsco.Core.Persistence.Policies;
using Jabsco.Core.Persistence.Runs;
using Jabsco.Core.Providers;
using Jabsco.Core.Providers.Claude;
using Jabsco.Core.Rdp;
using Jabsco.Core.Sessions;
using Jabsco.Core.Skills;
using Jabsco.Core.Transcripts;
using Microsoft.Extensions.Logging;
using NUlid;

namespace Jabsco.Ui.ViewModels;

public enum SidePanelTab { Chat, History, Settings }

public sealed record LogItem(string Icon, string Text);

public sealed class ContextScreenshotViewModel : IDisposable
{
    public Bitmap? Image { get; }

    public ContextScreenshotViewModel(byte[]? png)
    {
        if (png is { Length: > 0 })
        {
            using var ms = new MemoryStream(png);
            Image = new Bitmap(ms);
        }
    }

    public void Dispose() => Image?.Dispose();
}

public partial class SessionViewModel : ViewModelBase, IAsyncDisposable
{
    private readonly FreeRdpClient _client;
    private readonly ILoggerFactory _loggerFactory;
    private readonly JabscoDb? _db;
    private readonly int? _profileId;
    private CancellationTokenSource? _viewCts;
    private CancellationTokenSource? _agentCts;
    private Task? _viewTask;
    private ChatItemViewModel? _pendingActionItem;
    private List<ConversationTurn> _conversationHistory = [];

    // How many screenshots are currently in the model's context window.
    // Updated on every ScreenshotEvent based on strategy + conversation history.
    private int _screenshotsInContext;

    // Set by MainWindowViewModel.AddSession so disconnect closes the tab
    internal Action? CloseTab { get; set; }

    // Set by SessionView to show a confirmation dialog before disconnecting
    internal Func<Task<bool>>? ConfirmDisconnect { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ControlModeLabel))]
    [NotifyPropertyChangedFor(nameof(IsObserveMode))]
    [NotifyPropertyChangedFor(nameof(IsManualMode))]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    private ControlMode _controlMode = ControlMode.Observe;

    [ObservableProperty]
    private ConnectionState _state = ConnectionState.Connected;

    [ObservableProperty]
    private byte[]? _currentFrame;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PanelToggleLabel))]
    [NotifyPropertyChangedFor(nameof(PanelArrow))]
    private bool _isPanelVisible = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsChatPaneActive))]
    [NotifyPropertyChangedFor(nameof(IsHistoryPaneActive))]
    [NotifyPropertyChangedFor(nameof(IsSettingsPaneActive))]
    private SidePanelTab _activePanelTab = SidePanelTab.Chat;

    [ObservableProperty]
    private ToolPolicy? _selectedPolicy;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModelStrategyDescription))]
    private ModelStrategy _selectedModelStrategy = ModelStrategy.LatestOnly;

    public IReadOnlyList<ModelStrategy> AvailableModelStrategies { get; } =
        [ModelStrategy.LatestOnly, ModelStrategy.CacheAware];

    public string ModelStrategyDescription => SelectedModelStrategy switch
    {
        ModelStrategy.CacheAware => "Keeps last 3 screenshots and adds cache_control breakpoints to reduce token costs on long runs.",
        _ => "Sends only the current screenshot each turn. Simple and predictable."
    };

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendPromptCommand))]
    [NotifyPropertyChangedFor(nameof(CanInteract))]
    private bool _isAgentRunning;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SendPromptCommand))]
    private string _promptText = "";

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isCommandSuggestionsVisible;

    // History pane stats
    [ObservableProperty] private string _screenshotCountText = "0";
    [ObservableProperty] private string _screenshotTokenText = "—";
    [ObservableProperty] private string _cachedTokenText = "—";
    [ObservableProperty] private string _systemTokenText = "—";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ScreenshotsChevron))]
    private bool _isScreenshotsExpanded;

    public string ScreenshotsChevron => IsScreenshotsExpanded ? "▾" : "▸";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsZoomed))]
    private Bitmap? _zoomedImage;

    public bool IsZoomed => ZoomedImage != null;

    public ObservableCollection<ContextScreenshotViewModel> ContextScreenshots { get; } = [];

    public string Host { get; }
    public ObservableCollection<ChatItemViewModel> ChatItems { get; } = [];
    public ObservableCollection<LogItem> HistoryItems { get; } = [];
    public ObservableCollection<string> CommandSuggestions { get; } = [];
    public ObservableCollection<RdpOverlayItem> OverlayItems { get; } = [];
    public ObservableCollection<ToolPolicy> AvailablePolicies { get; } = [];

    private int _totalInputTokens;
    private int _totalOutputTokens;

    [ObservableProperty]
    private string _tokenCountText = "";

    public string ControlModeLabel => ControlMode == ControlMode.Manual ? "Manual" : "Observe";

    public bool IsObserveMode => ControlMode == ControlMode.Observe;
    public bool IsManualMode  => ControlMode == ControlMode.Manual;
    public string PanelToggleLabel => IsPanelVisible ? "Hide Chat" : "Show Chat";
    public string PanelArrow  => IsPanelVisible ? "›" : "‹";

    public bool IsChatPaneActive     => ActivePanelTab == SidePanelTab.Chat;
    public bool IsHistoryPaneActive  => ActivePanelTab == SidePanelTab.History;
    public bool IsSettingsPaneActive => ActivePanelTab == SidePanelTab.Settings;

    public SessionViewModel(FreeRdpClient client, string host, ILoggerFactory loggerFactory,
        JabscoDb? db = null, int? profileId = null)
    {
        _client = client;
        _loggerFactory = loggerFactory;
        _db = db;
        _profileId = profileId;
        Host = host;

        // Pre-populate strategy from config if set; user can change it in the settings pane.
        try
        {
            var cfg = ConfigLoader.Load();
            if (cfg.Agent.ModelStrategy.HasValue)
                SelectedModelStrategy = cfg.Agent.ModelStrategy.Value;
        }
        catch { /* malformed config — leave default */ }

        if (_db != null) _ = LoadPoliciesAsync();
    }

    private async Task LoadPoliciesAsync()
    {
        var policies = await _db!.ToolPolicies.GetAllAsync();
        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            AvailablePolicies.Clear();
            foreach (var p in policies) AvailablePolicies.Add(p);
            SelectedPolicy = policies.FirstOrDefault(p => p.Name == "default");
        });
    }

    public void StartLiveView()
    {
        _viewCts = new CancellationTokenSource();
        _viewTask = Task.Run(() => RunLiveViewAsync(_viewCts.Token));
    }

    private async Task RunLiveViewAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var png = await _client.CaptureScreenshotPngAsync(ct);
                Dispatcher.UIThread.Post(() => CurrentFrame = png);
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) { break; }
            catch { /* transient errors — keep polling */ }
        }
    }

    partial void OnPromptTextChanged(string value)
    {
        if (!value.StartsWith('/') || value.Contains(' ') || value.Contains('\n'))
        {
            CommandSuggestions.Clear();
            IsCommandSuggestionsVisible = false;
            return;
        }

        var prefix = value[1..];
        var matches = CommandLoader.List()
            .Where(n => n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));

        CommandSuggestions.Clear();
        foreach (var m in matches)
            CommandSuggestions.Add(m);

        IsCommandSuggestionsVisible = CommandSuggestions.Count > 0;
    }

    partial void OnControlModeChanged(ControlMode value)
    {
        var label = value == ControlMode.Manual ? "Manual" : "Observe";
        HistoryItems.Add(new LogItem("⇄", $"Mode → {label}"));
    }

    public void SelectSuggestion(string name)
    {
        PromptText = "/" + name + " ";
        IsCommandSuggestionsVisible = false;
    }

    [RelayCommand]
    private void TogglePanel() => IsPanelVisible = !IsPanelVisible;

    [RelayCommand]
    private void ToggleScreenshotsExpanded() => IsScreenshotsExpanded = !IsScreenshotsExpanded;

    [RelayCommand]
    private void ZoomIn(ContextScreenshotViewModel item) => ZoomedImage = item.Image;

    [RelayCommand]
    private void DismissZoom() => ZoomedImage = null;

    [RelayCommand]
    private void ShowChat()
    {
        ActivePanelTab = SidePanelTab.Chat;
        IsPanelVisible = true;
    }

    [RelayCommand]
    private void ShowHistory()
    {
        ActivePanelTab = SidePanelTab.History;
        IsPanelVisible = true;
    }

    [RelayCommand]
    private void ShowSettings()
    {
        ActivePanelTab = SidePanelTab.Settings;
        IsPanelVisible = true;
    }

    [RelayCommand]
    private void SetObserveMode() => ControlMode = ControlMode.Observe;

    [RelayCommand]
    private void SetManualMode() => ControlMode = ControlMode.Manual;

    [RelayCommand]
    private void StopAgent()
    {
        _agentCts?.Cancel();
    }

    [RelayCommand(CanExecute = nameof(CanSend))]
    private async Task SendPromptAsync()
    {
        var prompt = PromptText.Trim();
        if (string.IsNullOrEmpty(prompt)) return;

        PromptText = "";
        IsAgentRunning = true;

        ScreenshotCountText = "—";
        ScreenshotTokenText = "—";

        AddChat(ChatItemKind.UserPrompt, prompt);

        JabscoConfig config;
        try { config = ConfigLoader.Load(); }
        catch (InvalidDataException ex)
        {
            AddChat(ChatItemKind.Error, ex.Message);
            IsAgentRunning = false;
            return;
        }

        _agentCts = new CancellationTokenSource();
        var ct = _agentCts.Token;

        try
        {
            IComputerUseProvider provider;
            try { provider = ProviderFactory.Create(config); }
            catch (InvalidOperationException ex)
            {
                AddChat(ChatItemKind.Error, ex.Message);
                IsAgentRunning = false;
                return;
            }
            using var _disposeProvider = provider as IDisposable;
            SystemTokenText = $"≈ {Compact(provider.BuildSystemPrompt().Length / 4)}";

            var loop = new AgentLoop(_client, provider, new UiApprovalSink(ChatItems), _conversationHistory, new PolicyMatcher());
            var ac = config.Agent;
            var opts = new AgentOptions(
                MaxSteps: ac.MaxSteps ?? 50,
                PostActionDelay: TimeSpan.FromMilliseconds(ac.PostActionDelayMs ?? 800),
                TimeBudget: ac.TimeBudgetSeconds.HasValue ? TimeSpan.FromSeconds(ac.TimeBudgetSeconds.Value) : null,
                ToolPolicy: ac.ToolPolicy,
                ModelStrategy: SelectedModelStrategy);

            string resolvedPrompt;
            try
            {
                resolvedPrompt = CommandLoader.Resolve(prompt) ?? SkillLoader.Resolve(prompt);
            }
            catch (FileNotFoundException ex)
            {
                AddChat(ChatItemKind.Error, ex.Message);
                IsAgentRunning = false;
                return;
            }

            var runId = Ulid.NewUlid().ToString();
            var transcriptPath = TranscriptPaths.ForRun(runId);
            var runStarted = DateTimeOffset.UtcNow;

            if (_db != null)
                await _db.Runs.InsertAsync(new Run(runId, _profileId, Host, provider.ModelId,
                    runStarted, null, prompt, null, null, null, null, null, transcriptPath));

            await using var transcript = new TranscriptWriter(transcriptPath);
            FinalEvent? finalEvent = null;
            ErrorEvent? errorEvent = null;

            await foreach (var ev in loop.RunAsync(resolvedPrompt, opts, SelectedPolicy, ct))
            {
                // Capture terminal events first — transcript write uses None so a
                // cancelled ct doesn't swallow the FinalEvent on user-stop.
                if (ev is FinalEvent fe) finalEvent = fe;
                else if (ev is ErrorEvent ee) errorEvent = ee;
                await transcript.WriteAsync(ev, CancellationToken.None);
                Dispatcher.UIThread.Post(() => HandleAgentEvent(ev));
            }

            if (_db != null)
            {
                var endedAt = DateTimeOffset.UtcNow;
                if (finalEvent != null)
                    await _db.Runs.UpdateCompletionAsync(runId, endedAt,
                        string.IsNullOrEmpty(finalEvent.Response) ? null : finalEvent.Response,
                        finalEvent.Stats.StoppedReason, finalEvent.Stats.Steps,
                        finalEvent.Stats.InputTokens, finalEvent.Stats.OutputTokens);
                else if (errorEvent != null)
                    await _db.Runs.UpdateCompletionAsync(runId, endedAt,
                        null, StoppedReason.Error, null, null, null);
            }

            _conversationHistory = loop.History.ToList();
        }
        catch (OperationCanceledException)
        {
            // FinalEvent with UserCancel handles this; OCE here means an unexpected cancellation
        }
        catch (Exception ex)
        {
            AddChat(ChatItemKind.Error, ex.Message);
        }
        finally
        {
            // Resolve any action that was still in-flight when the loop ended
            if (_pendingActionItem != null)
            {
                _pendingActionItem.ActionState = ActionState.Failure;
                _pendingActionItem.Details = "Cancelled";
                _pendingActionItem = null;
            }
        }
        IsAgentRunning = false;
    }

    private bool CanSend() => !string.IsNullOrWhiteSpace(PromptText) && !IsAgentRunning;

    private void HandleAgentEvent(AgentEvent ev)
    {
        switch (ev)
        {
            case ScreenshotEvent s:
                CurrentFrame = s.PngBytes;
                _screenshotsInContext = SelectedModelStrategy == ModelStrategy.CacheAware
                    ? Math.Min(
                        _conversationHistory.Count(r => r.LastScreenshotPng != null) + 1,
                        ClaudeProvider.CacheAwareScreenshotWindow)
                    : 1;
                ScreenshotCountText = _screenshotsInContext.ToString();
                ScreenshotTokenText = $"≈ {Compact(_screenshotsInContext * EstimateImageTokens(s.PngBytes))}";
                RebuildContextScreenshots(s.PngBytes);
                break;

            case ThinkingEvent t:
                AddChat(ChatItemKind.Thinking, t.Text);
                break;

            case ActionEvent a:
                var actionItem = new ChatItemViewModel
                {
                    Kind = ChatItemKind.Action,
                    Text = DescribeAction(a.Action),
                    At = DateTimeOffset.UtcNow
                };
                _pendingActionItem = actionItem;
                ChatItems.Add(actionItem);
                AddOverlaysForAction(a.Action);
                break;

            case ToolResultEvent r:
                if (_pendingActionItem != null)
                {
                    var denied = r.Summary.StartsWith("denied", StringComparison.OrdinalIgnoreCase);
                    _pendingActionItem.ActionState = denied ? ActionState.Failure : ActionState.Success;
                    if (denied) _pendingActionItem.Details = "Denied";
                    _pendingActionItem = null;
                }
                break;

            case FinalEvent f:
                if (_pendingActionItem != null)
                {
                    _pendingActionItem.ActionState = ActionState.Failure;
                    _pendingActionItem = null;
                }
                ClearAgentCursor();

                if (f.Stats.StoppedReason == StoppedReason.UserCancel)
                    AddChat(ChatItemKind.AgentResponse, "— stopped —");
                else if (!string.IsNullOrEmpty(f.Response))
                    AddChat(ChatItemKind.AgentResponse, f.Response);

                _totalInputTokens  += f.Stats.InputTokens;
                _totalOutputTokens += f.Stats.OutputTokens;
                TokenCountText = $"in {Compact(_totalInputTokens)} · out {Compact(_totalOutputTokens)}";

                if (f.Stats.CachedInputTokens > 0)
                    CachedTokenText = $"≈ {Compact(f.Stats.CachedInputTokens)}";

                var secs = f.Stats.DurationMs / 1000.0;
                var cachedPart = f.Stats.CachedInputTokens > 0
                    ? $" · cached {Compact(f.Stats.CachedInputTokens)}"
                    : "";
                var stopLabel = f.Stats.StoppedReason switch
                {
                    StoppedReason.UserCancel  => " · stopped",
                    StoppedReason.StepBudget  => " · step limit",
                    StoppedReason.TimeBudget  => " · time limit",
                    _                         => ""
                };
                HistoryItems.Add(new LogItem("◆",
                    $"in {Compact(f.Stats.InputTokens)} · out {Compact(f.Stats.OutputTokens)}{cachedPart} · {f.Stats.Steps} steps · {secs:0.0}s{stopLabel}"));
                break;

            case ErrorEvent e:
                if (_pendingActionItem != null)
                {
                    _pendingActionItem.ActionState = ActionState.Failure;
                    _pendingActionItem.Details = e.Message;
                    _pendingActionItem.IsExpanded = true;
                    _pendingActionItem = null;
                }
                else
                {
                    ChatItems.Add(new ChatItemViewModel
                    {
                        Kind = ChatItemKind.Error,
                        Text = e.Message,
                        At = DateTimeOffset.UtcNow,
                        IsExpanded = true
                    });
                }
                HistoryItems.Add(new LogItem("!", $"Error: {e.Message}"));
                break;
        }
    }

    private void RebuildContextScreenshots(byte[] currentPng)
    {
        ZoomedImage = null; // clear before disposing bitmaps
        foreach (var item in ContextScreenshots) item.Dispose();
        ContextScreenshots.Clear();

        if (SelectedModelStrategy == ModelStrategy.CacheAware)
        {
            foreach (var turn in _conversationHistory
                .Where(t => t.LastScreenshotPng != null)
                .TakeLast(ClaudeProvider.CacheAwareScreenshotWindow - 1))
            {
                ContextScreenshots.Add(new ContextScreenshotViewModel(turn.LastScreenshotPng));
            }
        }
        ContextScreenshots.Add(new ContextScreenshotViewModel(currentPng));
    }

    private void AddChat(ChatItemKind kind, string text)
    {
        if (Dispatcher.UIThread.CheckAccess())
            ChatItems.Add(new ChatItemViewModel { Kind = kind, Text = text, At = DateTimeOffset.UtcNow });
        else
            Dispatcher.UIThread.Post(() => ChatItems.Add(new ChatItemViewModel { Kind = kind, Text = text, At = DateTimeOffset.UtcNow }));
    }

    private void AddOverlaysForAction(AgentAction action)
    {
        if (action is ClickAction c)
        {
            UpdateAgentCursor(c.X, c.Y);
            OverlayItems.Add(new RdpOverlayItem { Kind = OverlayKind.ClickRing, X = c.X, Y = c.Y, Lifetime = TimeSpan.FromMilliseconds(1500) });
        }
        else if (action is MouseMoveAction m)
        {
            UpdateAgentCursor(m.X, m.Y);
        }
        else if (action is DragAction d)
        {
            UpdateAgentCursor(d.EndX, d.EndY);
        }
    }

    private void UpdateAgentCursor(double x, double y)
    {
        var old = OverlayItems.FirstOrDefault(i => i.Kind == OverlayKind.AgentCursor);
        if (old != null) OverlayItems.Remove(old);
        OverlayItems.Add(new RdpOverlayItem { Kind = OverlayKind.AgentCursor, X = x, Y = y });
    }

    private void ClearAgentCursor()
    {
        var old = OverlayItems.FirstOrDefault(i => i.Kind == OverlayKind.AgentCursor);
        if (old != null) OverlayItems.Remove(old);
    }

    private static string DescribeAction(AgentAction action) => action switch
    {
        ClickAction c      => $"{c.Button} click at ({c.X}, {c.Y})" + (c.Clicks > 1 ? $" ×{c.Clicks}" : ""),
        MouseMoveAction m  => $"Move to ({m.X}, {m.Y})",
        DragAction d       => $"Drag ({d.StartX},{d.StartY}) → ({d.EndX},{d.EndY})",
        ScrollAction s     => $"Scroll {s.Direction} {s.Amount}× at ({s.X},{s.Y})",
        KeyAction k        => $"Key: {k.Keys}",
        TypeAction t       => $"Type: \"{t.Text[..Math.Min(t.Text.Length, 40)]}\"",
        ScreenshotAction   => "Screenshot",
        LoadSkillAction ls => $"Load skill: {ls.SkillName}",
        DoneAction         => "Done",
        _                  => action.GetType().Name
    };

    // Allow interaction in Manual mode, or in Observe mode when the agent is idle.
    public bool CanInteract => ControlMode == ControlMode.Manual ||
        (ControlMode == ControlMode.Observe && !IsAgentRunning);

    public async Task SendMouseClickAsync(MouseButton button, int x, int y)
    {
        if (!CanInteract) return;
        try { await _client.MouseClickAsync(button, x, y, CancellationToken.None); } catch { }
    }

    public async Task SendMouseMoveAsync(int x, int y)
    {
        if (!CanInteract) return;
        try { await _client.MouseMoveAsync(x, y, CancellationToken.None); } catch { }
    }

    public async Task SendScrollAsync(int x, int y, ScrollDirection direction, int amount)
    {
        if (!CanInteract) return;
        try { await _client.MouseScrollAsync(x, y, direction, amount, CancellationToken.None); } catch { }
    }

    public async Task SendKeyPressAsync(string chord)
    {
        if (!CanInteract) return;
        try { await _client.KeyPressAsync(chord, CancellationToken.None); } catch { }
    }

    public async Task SendTextAsync(string text)
    {
        if (!CanInteract) return;
        try { await _client.TypeTextAsync(text, CancellationToken.None); } catch { }
    }

    private static string Compact(int n) => n >= 1000 ? $"{n / 1000.0:0.#}k" : n.ToString("N0");

    // PNG IHDR: 8-byte sig + 4-byte chunk-length + 4-byte "IHDR" + 4-byte width + 4-byte height
    private static int EstimateImageTokens(byte[] png)
    {
        if (png.Length < 24) return 0;
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return w * h / 750;
    }

    [RelayCommand]
    private async Task DisconnectAsync()
    {
        if (ConfirmDisconnect != null && !await ConfirmDisconnect())
            return;

        _agentCts?.Cancel();
        _viewCts?.Cancel();
        if (_viewTask != null)
        {
            try { await _viewTask.ConfigureAwait(false); } catch { }
        }
        await _client.DisconnectAsync(CancellationToken.None);
        CloseTab?.Invoke();
    }

    public async ValueTask DisposeAsync()
    {
        _agentCts?.Cancel();
        _viewCts?.Cancel();
        if (_viewTask != null)
        {
            try { await _viewTask; } catch { }
        }
        await _client.DisposeAsync();
    }

    private sealed class UiApprovalSink : IApprovalSink
    {
        private readonly ObservableCollection<ChatItemViewModel> _chatItems;
        private readonly HashSet<string> _sessionAllowed = [];

        public UiApprovalSink(ObservableCollection<ChatItemViewModel> chatItems) => _chatItems = chatItems;

        public async Task<ToolDecision> RequestAsync(string tool, object payload, TimeSpan timeout, CancellationToken ct)
        {
            if (_sessionAllowed.Contains(tool))
                return ToolDecision.Allow;

            var item = new ChatItemViewModel
            {
                Kind = ChatItemKind.ApprovalRequest,
                Text = "Allow the above action?",
                At = DateTimeOffset.UtcNow
            };
            item.InitApproval();

            Dispatcher.UIThread.Post(() => _chatItems.Add(item));

            ApprovalChoice choice;
            try { choice = await item.ApprovalTask.WaitAsync(timeout, ct); }
            catch { return ToolDecision.Deny; }

            if (choice == ApprovalChoice.AllowSession)
                _sessionAllowed.Add(tool);

            Dispatcher.UIThread.Post(() => _chatItems.Remove(item));

            return choice == ApprovalChoice.Deny ? ToolDecision.Deny : ToolDecision.Allow;
        }
    }
}
