using Jabsco.Common.Events;
using Jabsco.Core.Config;
using Jabsco.Core.Providers;
using Jabsco.Core.Providers.Claude;

namespace Jabsco.Core.Tests;

// Covers ScreenshotPlan.Retained (which screenshots each strategy keeps) and
// ClaudeProvider.CacheBreakpoints (where cache_control lands).
public sealed class ScreenshotSelectionTests
{
    private static byte[] Png(byte b) => [b];

    private static ToolTurn Click(string id, byte[] png) =>
        new(new ClickAction(MouseButton.Left, 0, 0), "clicked", id, png);

    private static ToolTurn Shot(string id, byte[] png) =>
        new(new ScreenshotAction(), "screenshot", id, png);

    [Fact]
    public void ModelManaged_KeepsOnlyPromptsAndScreenshotActions()
    {
        var current = new List<ToolTurn> { Click("c1", Png(1)), Shot("s1", Png(2)) };
        var retained = ScreenshotPlan.Retained([], current, Png(9), ModelStrategy.ModelManaged);

        Assert.Contains("p:current", retained);
        Assert.Contains("t:s1", retained);
        Assert.DoesNotContain("t:c1", retained); // a click is not a screenshot the model asked for
    }

    [Fact]
    public void CacheAware_KeepsEveryTurn()
    {
        var current = new List<ToolTurn> { Click("c1", Png(1)), Shot("s1", Png(2)) };
        var retained = ScreenshotPlan.Retained([], current, Png(9), ModelStrategy.CacheAware);

        Assert.Contains("t:c1", retained);
        Assert.Contains("t:s1", retained);
    }

    [Fact]
    public void LatestOnly_KeepsOnlyTheMostRecent()
    {
        var current = new List<ToolTurn> { Click("c1", Png(1)), Shot("s1", Png(2)) };
        var retained = ScreenshotPlan.Retained([], current, Png(9), ModelStrategy.LatestOnly);

        Assert.Equal(["t:s1"], retained);
    }

    [Fact]
    public void CacheBreakpoints_MarkHistoryScreenshots()
    {
        var shot = Png(2);
        var prompt = Png(8);
        var round = new ConversationTurn("do it", [Shot("s1", shot)], "done", prompt);

        var cached = ClaudeProvider.CacheBreakpoints([round]);

        Assert.Contains(shot, cached);
        Assert.Contains(prompt, cached);
    }

    [Fact]
    public void CacheBreakpoints_CapAtWindowNewestFirst()
    {
        byte[] s1 = Png(1), s2 = Png(2), s3 = Png(3), s4 = Png(4);
        var round = new ConversationTurn("do it",
            [Shot("a", s1), Shot("b", s2), Shot("c", s3), Shot("d", s4)], "done");

        var cached = ClaudeProvider.CacheBreakpoints([round]);

        Assert.Equal(ScreenshotPlan.Window, cached.Count);
        Assert.Contains(s4, cached);
        Assert.Contains(s3, cached);
        Assert.Contains(s2, cached);
        Assert.DoesNotContain(s1, cached); // oldest falls outside the window
    }
}
