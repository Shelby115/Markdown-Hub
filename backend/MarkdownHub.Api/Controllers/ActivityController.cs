using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

// IpAddress is included even in the summary (not just detail) because unauthenticated events
// (UserId null) have no username to show - section 2.6 requires the IP to be the *primary*
// identifier for those in the main list, not something hidden behind an expand action.
public record ActivitySummaryDto(int Id, DateTimeOffset Timestamp, int? UserId, string? Username, string Action,
    string? ObjectType, int? ObjectId, string? TargetPath, int OccurrenceCount, DateTimeOffset? LastOccurredAtUtc,
    int? RelatedVersionId, string? IpAddress);

public record ActivityDetailDto(int Id, DateTimeOffset Timestamp, int? UserId, string? Username, string Action,
    string? ObjectType, int? ObjectId, string? TargetPath, string? Details, string? IpAddress,
    int OccurrenceCount, DateTimeOffset? LastOccurredAtUtc, int? RelatedVersionId);

public record ActivityPageDto(IReadOnlyList<ActivitySummaryDto> Items, int TotalCount, int Page, int PageSize);

/// <summary>
/// Activity Log API - admin-only, per Activity-And-History.md section 2.7 ("Regular users must
/// not have access to the global activity log"). Everything here operates on AuditLogEntry, the
/// same table the original admin-action audit trail already used.
/// </summary>
[ApiController]
[Route("api/admin/activity")]
[Authorize(Policy = "RequireAdministrator")]
public class ActivityController : ControllerBase
{
    private const int MaxPageSize = 200;

    private readonly AppDbContext _db;
    private readonly HistorySettingsService _settings;

    public ActivityController(AppDbContext db, HistorySettingsService settings)
    {
        _db = db;
        _settings = settings;
    }

    [HttpGet]
    public async Task<IActionResult> Query(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] int? userId,
        [FromQuery] string? action,
        [FromQuery] string? objectSearch,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        var retentionDays = await _settings.GetActivityRetentionDaysAsync(ct);
        var defaultDays = await _settings.GetActivityDefaultDaysAsync(ct);
        var now = DateTimeOffset.UtcNow;

        // Defaults to the most recent ActivityDefaultDays; an explicit `from` may reach further
        // back, but never further than the configured retention window - older rows aren't
        // guaranteed to still exist once cleanup has run.
        var effectiveFrom = from ?? now.AddDays(-defaultDays);
        var retentionFloor = now.AddDays(-retentionDays);
        if (effectiveFrom < retentionFloor) effectiveFrom = retentionFloor;
        var effectiveTo = to ?? now;

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // The Timestamp (DateTimeOffset) range can't be translated to SQL by the SQLite
        // provider (see AuditLogService for the same limitation) - the equality/Contains filters
        // below are applied server-side as usual, but the date range and pagination happen
        // client-side afterward. AuditLog is already retention-bounded (30 days by default), so
        // this stays a bounded fetch, not an unbounded table scan.
        var query = _db.AuditLog.AsQueryable();
        if (userId is not null) query = query.Where(a => a.AppUserId == userId);
        if (!string.IsNullOrWhiteSpace(action)) query = query.Where(a => a.Action == action);
        if (!string.IsNullOrWhiteSpace(objectSearch))
            query = query.Where(a => a.TargetPath != null && a.TargetPath.Contains(objectSearch));

        var candidates = await query.OrderByDescending(a => a.Id).ToListAsync(ct);
        var filtered = candidates.Where(a => a.Timestamp >= effectiveFrom && a.Timestamp <= effectiveTo).ToList();

        var totalCount = filtered.Count;
        var entries = filtered.Skip((page - 1) * pageSize).Take(pageSize).ToList();

        var userIds = entries.Where(e => e.AppUserId != null).Select(e => e.AppUserId!.Value).Distinct().ToList();
        var usernames = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        var items = entries.Select(e => new ActivitySummaryDto(
            e.Id, e.Timestamp, e.AppUserId, e.AppUserId is { } id && usernames.TryGetValue(id, out var n) ? n : null,
            e.Action, e.ObjectType, e.ObjectId, e.TargetPath, e.OccurrenceCount, e.LastOccurredAtUtc, e.RelatedVersionId,
            e.IpAddress
        )).ToList();

        return Ok(new ActivityPageDto(items, totalCount, page, pageSize));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetDetail(int id, CancellationToken ct)
    {
        var entry = await _db.AuditLog.FirstOrDefaultAsync(a => a.Id == id, ct);
        if (entry is null) return NotFound();

        var username = entry.AppUserId is { } userId ? (await _db.Users.FindAsync([userId], ct))?.Username : null;
        return Ok(new ActivityDetailDto(
            entry.Id, entry.Timestamp, entry.AppUserId, username, entry.Action, entry.ObjectType, entry.ObjectId,
            entry.TargetPath, entry.Details, entry.IpAddress, entry.OccurrenceCount, entry.LastOccurredAtUtc, entry.RelatedVersionId
        ));
    }

    /// <summary>Distinct action types seen so far, for the filter dropdown.</summary>
    [HttpGet("action-types")]
    public async Task<IActionResult> GetActionTypes(CancellationToken ct)
    {
        var actions = await _db.AuditLog.Select(a => a.Action).Distinct().OrderBy(a => a).ToListAsync(ct);
        return Ok(actions);
    }
}
