using Jabsco.Core.Providers;

namespace Jabsco.Core.Tests;

// Covers the 25-turn sawtooth used by the cache strategies: keep everything until a
// prune boundary, then drop all but the latest CacheAwareScreenshotWindow.
public sealed class ScreenshotRetentionTests
{
    private static List<int> Kept(int[] positions, int totalTurns)
    {
        var keep = ScreenshotPlan.RetainedScreenshots(positions, totalTurns);
        var result = new List<int>();
        for (int i = 0; i < keep.Length; i++)
            if (keep[i]) result.Add(positions[i]);
        return result;
    }

    [Fact]
    public void Empty_ReturnsEmpty()
    {
        Assert.Empty(ScreenshotPlan.RetainedScreenshots([], 0));
    }

    [Fact]
    public void BeforeFirstPrune_KeepsEverything()
    {
        int[] positions = [0, 4, 9, 17, 23];
        Assert.Equal(positions, Kept(positions, 24));
    }

    [Fact]
    public void AtPruneBoundary_KeepsLatestThree()
    {
        int[] positions = [0, 5, 10, 20, 24];
        Assert.Equal([10, 20, 24], Kept(positions, 25));
    }

    [Fact]
    public void PastPruneBoundary_KeepsSurvivorsPlusNewSinceBoundary()
    {
        // Latest three at/before turn 25 survive (10, 20, 24); everything after is kept (28).
        int[] positions = [0, 5, 10, 20, 24, 28];
        Assert.Equal([10, 20, 24, 28], Kept(positions, 30));
    }

    [Fact]
    public void SecondCycle_PrunesAgainAtFifty()
    {
        int[] positions = [10, 20, 24, 28, 40, 49];
        Assert.Equal([28, 40, 49], Kept(positions, 50));
    }

    [Fact]
    public void FewerThanWindowBeforeBoundary_KeepsWhatExists()
    {
        int[] positions = [3, 40, 48];
        Assert.Equal([3, 40, 48], Kept(positions, 50));
    }
}
