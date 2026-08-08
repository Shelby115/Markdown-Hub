using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class PoolFillStatusTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 8, 12, 0, 0, TimeSpan.Zero);

    private static GenerationPool Pool(bool enabled = true, int target = 20) =>
        new() { Name = "Interactible", Enabled = enabled, TargetCount = target };

    private static GenerationPoolSettings Settings(bool paused = false, string? start = null, string? end = null) =>
        new(paused, start, end, IntervalSeconds: 60, UsedEntryRetentionDays: 90);

    private static PoolFillStatus For(GenerationPool pool, int ready, GenerationPoolSettings settings, bool filling = false) =>
        PoolFillStatus.For(pool, ready, settings, Noon, filling);

    [Fact]
    public void FillingRightNow_OutranksEverythingElse()
    {
        var status = For(Pool(), ready: 3, Settings(paused: true), filling: true);

        Assert.Equal("Generating", status.Label);
    }

    [Fact]
    public void PoolSwitchedOff_SaysSoAndPointsAtTheSetting()
    {
        var status = For(Pool(enabled: false), ready: 0, Settings());

        Assert.Equal("Off", status.Label);
        Assert.Contains("turned off for this pool", status.Reason);
    }

    [Fact]
    public void ZeroTarget_ReadsAsOffRatherThanFull()
    {
        var status = For(Pool(target: 0), ready: 0, Settings());

        Assert.Equal("Off", status.Label);
        Assert.Contains("target is 0 entries", status.Reason);
    }

    [Fact]
    public void AtTarget_IsFull()
    {
        var status = For(Pool(target: 20), ready: 20, Settings());

        Assert.Equal("Full", status.Label);
        Assert.Contains("All 20 entries are ready", status.Reason);
    }

    [Fact]
    public void Paused_ExplainsThatItIsGlobalAndHowManyAreOutstanding()
    {
        var status = For(Pool(target: 20), ready: 3, Settings(paused: true));

        Assert.Equal("Paused", status.Label);
        Assert.Contains("17 more to generate", status.Reason);
        Assert.Contains("paused for every pool", status.Reason);
    }

    [Fact]
    public void OutsideTheWindow_NamesTheWindowAndTheCurrentServerTime()
    {
        var status = For(Pool(), ready: 3, Settings(start: "22:00", end: "06:00"));

        Assert.Equal("Waiting", status.Label);
        Assert.Contains("22:00-06:00 UTC", status.Reason);
        Assert.Contains("It is now 12:00 UTC", status.Reason);
    }

    [Fact]
    public void EnabledAndAllowed_IsQueuedWithTheOutstandingCount()
    {
        var status = For(Pool(target: 20), ready: 3, Settings());

        Assert.Equal("Queued", status.Label);
        Assert.Contains("17 more to generate", status.Reason);
        // The wait itself is shown as a live countdown dial, so the text doesn't quote the interval.
        Assert.DoesNotContain("60 seconds", status.Reason);
    }

    [Fact]
    public void APoolThatIsFull_ReadsAsFullEvenWhilePausedOrOutOfWindow()
    {
        // "Paused" on a pool that has nothing left to do would be a misleading thing to worry about.
        Assert.Equal("Full", For(Pool(target: 5), ready: 5, Settings(paused: true)).Label);
        Assert.Equal("Full", For(Pool(target: 5), ready: 5, Settings(start: "22:00", end: "06:00")).Label);
    }
}
