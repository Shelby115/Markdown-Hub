using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Why a pool is or isn't being filled right now. Resolved server-side because the answer depends
/// on four separate things (the pool's own switch, its fill level, the global pause, and the
/// allowed window) and "Generating: No" with no explanation is the least useful thing the admin
/// page could say. Reason is written to be read as a tooltip.
/// </summary>
public record PoolFillStatus(string Label, string Reason)
{
    public static PoolFillStatus For(
        GenerationPool pool, int readyCount, GenerationPoolSettings settings, DateTimeOffset nowUtc, bool isFillingNow)
    {
        if (isFillingNow)
        {
            return new("Generating", "Writing a new entry right now.");
        }

        if (!pool.Enabled)
        {
            return new("Off", "Background generation is turned off for this pool. Edit it and tick “Generate entries for this pool in the background” to start.");
        }

        if (pool.TargetCount == 0)
        {
            return new("Off", "This pool's target is 0 entries, so nothing will be generated. Raise it to start filling.");
        }

        if (readyCount >= pool.TargetCount)
        {
            return new("Full", $"All {pool.TargetCount} entries are ready. The generator will top the pool up again as entries are used.");
        }

        if (settings.Paused)
        {
            return new("Paused", $"{pool.TargetCount - readyCount} more to generate, but the background generator is paused for every pool.");
        }

        if (!settings.IsWithinWindow(nowUtc))
        {
            return new("Waiting", $"{pool.TargetCount - readyCount} more to generate, but generation is only allowed between {settings.WindowDescription}. It is now {nowUtc.UtcDateTime:HH:mm} UTC.");
        }

        return new("Queued", $"{pool.TargetCount - readyCount} more to generate - the next one starts within {settings.IntervalSeconds} seconds.");
    }
}
