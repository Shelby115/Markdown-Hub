using System.Globalization;

namespace MarkdownHub.Api.Services;

/// <summary>
/// App-wide controls for the background pool generator. Window times are "HH:mm" in UTC (the same
/// clock every other scheduled job in this app runs on) and both being null means "any time".
/// </summary>
public record GenerationPoolSettings(bool Paused, string? WindowStartUtc, string? WindowEndUtc, int IntervalSeconds, int UsedEntryRetentionDays)
{
    /// <summary>Whether the generator is allowed to run right now: not paused, and inside the
    /// configured window. A window whose end is before its start wraps past midnight.</summary>
    public bool IsAllowedAt(DateTimeOffset nowUtc)
    {
        if (Paused)
        {
            return false;
        }

        if (!TryParseTime(WindowStartUtc, out var start) || !TryParseTime(WindowEndUtc, out var end) || start == end)
        {
            return true;
        }

        var now = nowUtc.UtcDateTime.TimeOfDay;
        return start < end
            ? now >= start && now < end
            : now >= start || now < end;
    }

    /// <summary>True for a blank value too - an unset half of the window means "no window".</summary>
    public static bool IsValidTime(string? value) => string.IsNullOrWhiteSpace(value) || TryParseTime(value, out _);

    private static bool TryParseTime(string? value, out TimeSpan time)
    {
        time = default;
        return !string.IsNullOrWhiteSpace(value)
            && TimeSpan.TryParseExact(value.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out time);
    }
}
