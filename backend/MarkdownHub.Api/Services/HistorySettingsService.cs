using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Resolves the three retention/display-window settings for the Version History and Activity
/// Log feature. Same DB-override-over-config-default pattern as OllamaAiService's model
/// resolution: an admin can change these at runtime (AppSetting rows) without a redeploy;
/// appsettings.json/.env values are only the fallback default.
/// </summary>
public class HistorySettingsService
{
    public const string VersionRetentionDaysKey = "History.VersionRetentionDays";
    public const string ActivityRetentionDaysKey = "History.ActivityRetentionDays";
    public const string ActivityDefaultDaysKey = "History.ActivityDefaultDays";

    // Sanity bounds - retention/window values are always small positive integers of days.
    // Zero would mean "keep nothing," which is a legitimate (if unusual) admin choice; negative
    // or absurdly large values are almost certainly a mistake, so they're rejected up front
    // rather than silently accepted and causing confusing cleanup behavior later.
    public const int MaxDays = 3650; // ~10 years

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;

    public HistorySettingsService(AppDbContext db, IConfiguration config)
    {
        _db = db;
        _config = config;
    }

    public async Task<int> GetVersionRetentionDaysAsync(CancellationToken ct = default) =>
        await ResolveAsync(VersionRetentionDaysKey, "History:VersionRetentionDays", 3, ct);

    public async Task<int> GetActivityRetentionDaysAsync(CancellationToken ct = default) =>
        await ResolveAsync(ActivityRetentionDaysKey, "History:ActivityRetentionDays", 30, ct);

    public async Task<int> GetActivityDefaultDaysAsync(CancellationToken ct = default) =>
        await ResolveAsync(ActivityDefaultDaysKey, "History:ActivityDefaultDays", 14, ct);

    public async Task<HistorySettings> GetAllAsync(CancellationToken ct = default) => new(
        await GetVersionRetentionDaysAsync(ct),
        await GetActivityRetentionDaysAsync(ct),
        await GetActivityDefaultDaysAsync(ct)
    );

    /// <summary>Throws <see cref="ArgumentOutOfRangeException"/> if the value is out of bounds.</summary>
    public static void Validate(int days)
    {
        if (days < 0 || days > MaxDays)
            throw new ArgumentOutOfRangeException(nameof(days), $"Must be between 0 and {MaxDays} days.");
    }

    public async Task SetAsync(string key, int days, CancellationToken ct = default)
    {
        Validate(days);
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting is null)
        {
            _db.Settings.Add(new AppSetting { Key = key, Value = days.ToString() });
        }
        else
        {
            setting.Value = days.ToString();
        }
        await _db.SaveChangesAsync(ct);
    }

    private async Task<int> ResolveAsync(string key, string configPath, int hardDefault, CancellationToken ct)
    {
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (setting?.Value is not null && int.TryParse(setting.Value, out var stored) && stored >= 0)
            return stored;
        return _config.GetValue<int?>(configPath) ?? hardDefault;
    }
}

public record HistorySettings(int VersionRetentionDays, int ActivityRetentionDays, int ActivityDefaultDays);
