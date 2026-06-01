using Jabsco.Common.Events;
using Jabsco.Core.Config;

namespace Jabsco.Core.Providers;

// Provider-agnostic screenshot retention. Decides which screenshots a strategy keeps so
// AgentLoop can free the rest; once a screenshot is gone, its mere presence on a turn or
// round is the signal to attach it, so providers don't repeat this logic.
//
// Keys identify the message a screenshot belongs to: "p:{round}" / "p:current" for the
// screenshot that follows a user prompt, "t:{toolUseId}" for a tool result.
public static class ScreenshotPlan
{
    // Screenshots kept in context after a prune.
    public const int Window = 3;
    // Turns between prunes for the cache strategies. The prefix stays byte-stable between
    // prunes so the cache keeps hitting, then drops to the latest Window.
    public const int PruneInterval = 25;

    private sealed record Point(int Position, string Key);

    // ModelManaged only keeps screenshots the model explicitly asked for; the others keep
    // whatever was captured after each action.
    public static bool Records(AgentAction action, ModelStrategy strategy) =>
        strategy is not ModelStrategy.ModelManaged || action is ScreenshotAction;

    // The message keys whose screenshots the strategy still keeps. AgentLoop frees any
    // screenshot whose key is absent.
    public static HashSet<string> Retained(
        IReadOnlyList<ConversationTurn> history,
        IReadOnlyList<ToolTurn> currentTurns,
        byte[]? currentPromptPng,
        ModelStrategy strategy)
    {
        var points = new List<Point>();
        int turnsSeen = 0;
        for (int r = 0; r < history.Count; r++)
        {
            var round = history[r];
            if (round.PromptScreenshotPng != null)
                points.Add(new Point(turnsSeen, $"p:{r}"));
            for (int j = 0; j < round.Turns.Count; j++)
                if (round.Turns[j].ScreenshotPng != null && Records(round.Turns[j].Action, strategy))
                    points.Add(new Point(turnsSeen + j + 1, $"t:{round.Turns[j].ToolUseId}"));
            turnsSeen += round.Turns.Count;
        }

        int currentBase = turnsSeen;
        if (currentPromptPng != null)
            points.Add(new Point(currentBase, "p:current"));
        for (int i = 0; i < currentTurns.Count; i++)
            if (currentTurns[i].ScreenshotPng != null && Records(currentTurns[i].Action, strategy))
                points.Add(new Point(currentBase + i + 1, $"t:{currentTurns[i].ToolUseId}"));
        int totalTurns = turnsSeen + currentTurns.Count;

        bool[] keep = strategy is ModelStrategy.LatestOnly
            ? LatestOnly(points.Count)
            : RetainedScreenshots(points.Select(p => p.Position).ToList(), totalTurns);

        var result = new HashSet<string>();
        for (int i = 0; i < points.Count; i++)
            if (keep[i]) result.Add(points[i].Key);
        return result;
    }

    // LatestOnly keeps only the most recent screenshot.
    private static bool[] LatestOnly(int count)
    {
        var keep = new bool[count];
        if (count > 0) keep[^1] = true;
        return keep;
    }

    // Keep every screenshot captured since the last prune boundary, plus the latest
    // Window from before it. Pruning is monotonic: a dropped screenshot is never wanted
    // again, so its bytes are safe to free.
    public static bool[] RetainedScreenshots(IReadOnlyList<int> turnPositions, int totalTurns)
    {
        int prune = totalTurns / PruneInterval * PruneInterval;
        var keep = new bool[turnPositions.Count];
        int survivors = 0;
        for (int i = turnPositions.Count - 1; i >= 0; i--)
        {
            if (turnPositions[i] > prune)
                keep[i] = true;
            else if (survivors < Window)
            {
                keep[i] = true;
                survivors++;
            }
        }
        return keep;
    }
}
