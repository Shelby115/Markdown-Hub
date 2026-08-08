using System.Globalization;

namespace MarkdownHub.Api.Services;

/// <summary>
/// App-wide controls for the background pool generator. Window times are "HH:mm" in UTC (the same
/// clock every other scheduled job in this app runs on) and both being null means "any time".
/// </summary>
public record GenerationPoolSettings(bool Paused, string? WindowStartUtc, string? WindowEndUtc, int IntervalSeconds, int UsedEntryRetentionDays)
{
    /// <summary>Whether the generator is allowed to run right now: not paused, and inside the
    /// configured window.</summary>
    public bool IsAllowedAt(DateTimeOffset nowUtc) => !Paused && IsWithinWindow(nowUtc);

    /// <summary>The window half of <see cref="IsAllowedAt"/>, kept separate so the admin page can
    /// say *which* reason is stopping a pool rather than just that something is. A window whose
    /// end is before its start wraps past midnight.</summary>
    public bool IsWithinWindow(DateTimeOffset nowUtc)
    {
        if (!TryParseTime(WindowStartUtc, out var start) || !TryParseTime(WindowEndUtc, out var end) || start == end)
        {
            return true;
        }

        var now = nowUtc.UtcDateTime.TimeOfDay;
        return start < end
            ? now >= start && now < end
            : now >= start || now < end;
    }

    /// <summary>Null when no window is set - i.e. generation is allowed at any hour.</summary>
    public string? WindowDescription =>
        TryParseTime(WindowStartUtc, out var start) && TryParseTime(WindowEndUtc, out var end) && start != end
            ? $"{WindowStartUtc!.Trim()}-{WindowEndUtc!.Trim()} UTC"
            : null;

    /// <summary>True for a blank value too - an unset half of the window means "no window".</summary>
    public static bool IsValidTime(string? value) => string.IsNullOrWhiteSpace(value) || TryParseTime(value, out _);

    private static bool TryParseTime(string? value, out TimeSpan time)
    {
        time = default;
        return !string.IsNullOrWhiteSpace(value)
            && TimeSpan.TryParseExact(value.Trim(), "hh\\:mm", CultureInfo.InvariantCulture, out time);
    }
}
