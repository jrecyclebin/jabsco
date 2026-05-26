using Jabsco.Common.Events;

namespace Jabsco.Ui.ViewModels;

// Used by ReplayViewModel for replay event display
public class EventItemViewModel : ViewModelBase
{
    public string Icon { get; }
    public string Label { get; }
    public string Detail { get; }
    public DateTimeOffset At { get; }
    public int Step { get; }

    public EventItemViewModel(AgentEvent ev)
    {
        At = ev.At;
        Step = ev.Step;
        (Icon, Label, Detail) = ev switch
        {
            ThinkingEvent t        => ("💭", "Thinking",   Truncate(t.Text, 120)),
            ActionEvent a          => ("▶",  "Action",     ActionLabel(a.Action)),
            ToolResultEvent r      => ("✓",  "Result",     Truncate(r.Summary, 80)),
            FinalEvent f           => ("🏁", "Done",       Truncate(f.Response, 120)),
            ErrorEvent e           => ("✗",  "Error",      Truncate(e.Message, 80)),
            ConnectionEvent c      => ("🔌", "Connection", c.State.ToString()),
            ApprovalRequestEvent a => ("❓", "Approval",   a.Tool),
            _                      => ("·",  ev.GetType().Name, "")
        };
    }

    private static string ActionLabel(AgentAction action) => action.GetType().Name.Replace("Action", "");

    private static string Truncate(string s, int max) =>
        s.Length <= max ? s : s[..max] + "…";
}
