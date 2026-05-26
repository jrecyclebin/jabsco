using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace Jabsco.Ui.ViewModels;

public enum ChatItemKind { UserPrompt, Thinking, Action, AgentResponse, Error, ApprovalRequest }
public enum ActionState { Pending, Success, Failure }
public enum ApprovalChoice { AllowOnce, AllowSession, Deny }

public partial class ChatItemViewModel : ObservableObject
{
    public ChatItemKind Kind { get; init; }
    public string Text { get; init; } = "";
    public DateTimeOffset At { get; init; } = DateTimeOffset.UtcNow;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ActionIcon), nameof(BubbleBackground), nameof(LabelColor))]
    private ActionState _actionState = ActionState.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    private string? _details;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ExpandLabel))]
    private bool _isExpanded;

    [ObservableProperty]
    private bool _showApprovalButtons;

    private TaskCompletionSource<ApprovalChoice>? _approvalTcs;

    public Task<ApprovalChoice> ApprovalTask => _approvalTcs!.Task;

    public void InitApproval()
    {
        _approvalTcs = new TaskCompletionSource<ApprovalChoice>();
        ShowApprovalButtons = true;
    }

    public bool IsActionItem       => Kind == ChatItemKind.Action;
    public bool IsUserPrompt       => Kind == ChatItemKind.UserPrompt;
    public bool IsApprovalRequest  => Kind == ChatItemKind.ApprovalRequest;
    public bool IsMarkdownKind     => Kind is ChatItemKind.UserPrompt or ChatItemKind.AgentResponse;
    public bool HasDetails         => !string.IsNullOrEmpty(Details);
    public string ExpandLabel      => IsExpanded ? "Hide ▲" : "Details ▼";

    public double TextSize   => Kind is ChatItemKind.Action or ChatItemKind.ApprovalRequest ? 12 : 16;
    public string SenderLabel => Kind == ChatItemKind.UserPrompt ? "You" : "Agent";
    public string MetaText   => $"{SenderLabel} · {At.ToLocalTime():h:mm tt}";

    public string ActionIcon => ActionState switch
    {
        ActionState.Pending => "⚙",
        ActionState.Success => "✓",
        ActionState.Failure => "✗",
        _                  => "⚙"
    };

    public string KindLabel => Kind switch
    {
        ChatItemKind.Thinking        => "Thinking",
        ChatItemKind.Error           => "Error",
        ChatItemKind.ApprovalRequest => "Approval Required",
        _                            => ""
    };

    public string FontFamily => Kind == ChatItemKind.AgentResponse ? "Monospace" : "Default";
    public bool ShowLabel    => Kind is ChatItemKind.Thinking or ChatItemKind.Error or ChatItemKind.ApprovalRequest;

    public string BubbleBackground => Kind == ChatItemKind.Action
        ? ActionState switch
        {
            ActionState.Pending => "#48FFF6D8",
            ActionState.Success => "#4FC8F7D2",
            ActionState.Failure => "#55FFB3B3",
            _                  => "#48FFF6D8"
        }
        : Kind switch
        {
            ChatItemKind.UserPrompt     => "#4A7DB8E8",
            ChatItemKind.Thinking       => "#30FFFFFF",
            ChatItemKind.AgentResponse  => "#38FFFFFF",
            ChatItemKind.Error          => "#55FFB3B3",
            ChatItemKind.ApprovalRequest => "#48FFE8A0",
            _                           => "#38FFFFFF"
        };

    public string LabelColor => Kind == ChatItemKind.Action
        ? ActionState switch
        {
            ActionState.Pending => "#FFE6A3",
            ActionState.Success => "#61E08A",
            ActionState.Failure => "#FF9A9A",
            _                  => "#FFE6A3"
        }
        : Kind switch
        {
            ChatItemKind.UserPrompt     => "#F7FBFF",
            ChatItemKind.Thinking       => "#DCE8F6",
            ChatItemKind.AgentResponse  => "#F7FBFF",
            ChatItemKind.Error          => "#FFD6D6",
            ChatItemKind.ApprovalRequest => "#FFE6A3",
            _                           => "#F7FBFF"
        };

    [RelayCommand]
    private void ToggleExpanded() => IsExpanded = !IsExpanded;

    [RelayCommand]
    private void AllowOnce()
    {
        ShowApprovalButtons = false;
        _approvalTcs?.TrySetResult(ApprovalChoice.AllowOnce);
    }

    [RelayCommand]
    private void AllowSession()
    {
        ShowApprovalButtons = false;
        _approvalTcs?.TrySetResult(ApprovalChoice.AllowSession);
    }

    [RelayCommand]
    private void Deny()
    {
        ShowApprovalButtons = false;
        _approvalTcs?.TrySetResult(ApprovalChoice.Deny);
    }
}
