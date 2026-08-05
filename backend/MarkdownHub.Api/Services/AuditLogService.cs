using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Records who did what to the AuditLog table - the shared backbone for both the original
/// admin-action audit trail and the Activity Log feature. Best-effort: a logging failure should
/// never block the underlying action, so callers await this after the real change is already
/// saved. Every entry automatically captures the caller's IP address from the current request.
/// </summary>
public class AuditLogService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuditLogService(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    private string? CurrentIpAddress => _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress?.ToString();

    public async Task LogAsync(int? actingAppUserId, string action, string? targetPath = null, string? details = null, CancellationToken ct = default)
    {
        _db.AuditLog.Add(new AuditLogEntry
        {
            AppUserId = actingAppUserId,
            Action = action,
            TargetPath = targetPath,
            Details = details,
            IpAddress = CurrentIpAddress,
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Richer variant for Version History / Activity Log call sites that also need to
    /// tag the affected object's type/id and/or link a document-modification event to the
    /// DocumentVersion it produced, so the activity UI can jump straight to a before/after diff.</summary>
    public async Task LogEventAsync(int? actingAppUserId, string action, string? targetPath, string? objectType,
        int? objectId, string? details = null, int? relatedVersionId = null, CancellationToken ct = default)
    {
        _db.AuditLog.Add(new AuditLogEntry
        {
            AppUserId = actingAppUserId,
            Action = action,
            TargetPath = targetPath,
            Details = details,
            ObjectType = objectType,
            ObjectId = objectId,
            RelatedVersionId = relatedVersionId,
            IpAddress = CurrentIpAddress,
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// For events that can occur in rapid, repetitive bursts from the same source (e.g. a client
    /// repeatedly sending a rejected auth token) - coalesces consecutive occurrences of the same
    /// action from the same IP within <paramref name="groupingWindow"/> into a single row
    /// (bumping OccurrenceCount and extending LastOccurredAtUtc) instead of one row per
    /// occurrence. Each event is still logged as exactly what it is; only genuinely repetitive,
    /// frequent occurrences get collapsed for display, and the full occurrence count/window
    /// remains inspectable in the details.
    /// </summary>
    public async Task LogGroupedAsync(string action, string? targetPath, string? objectType, string? ipAddress,
        string? details, TimeSpan groupingWindow, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow - groupingWindow;
        // The Timestamp (DateTimeOffset) comparison can't be translated to SQL by the SQLite
        // provider - fetch the single most recent same-action/same-IP row (Id ordering is a
        // plain int comparison, which does translate) and check its window client-side instead.
        var mostRecent = await _db.AuditLog
            .Where(a => a.Action == action && a.IpAddress == ipAddress)
            .OrderByDescending(a => a.Id)
            .FirstOrDefaultAsync(ct);
        var recent = mostRecent is not null && mostRecent.Timestamp >= cutoff ? mostRecent : null;

        if (recent is not null)
        {
            recent.OccurrenceCount += 1;
            recent.LastOccurredAtUtc = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return;
        }

        _db.AuditLog.Add(new AuditLogEntry
        {
            Action = action,
            TargetPath = targetPath,
            ObjectType = objectType,
            IpAddress = ipAddress,
            Details = details,
            OccurrenceCount = 1,
        });
        await _db.SaveChangesAsync(ct);
    }

    /// <summary>Permanently removes activity entries older than <paramref name="retentionDays"/>.
    /// Safe to run repeatedly.</summary>
    public async Task<int> CleanupExpiredAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        // Same DateTimeOffset-translation limitation as VersionService.CleanupExpiredVersionsAsync
        // - filter client-side. This table is already retention-bounded (30 days by default).
        var all = await _db.AuditLog.ToListAsync(ct);
        var stale = all.Where(a => a.Timestamp < cutoff).ToList();
        if (stale.Count == 0) return 0;
        _db.AuditLog.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
        return stale.Count;
    }
}
