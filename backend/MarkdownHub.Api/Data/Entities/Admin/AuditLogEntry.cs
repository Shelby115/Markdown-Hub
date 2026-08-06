namespace MarkdownHub.Api.Data.Entities.Admin;

/// <summary>
/// A single admin-audit/activity-log entry - the shared backbone for both the original
/// admin-action audit trail and the broader Activity Log feature (auth, file/folder,
/// settings/permission events). Deliberately one flat table with a structured `Details`
/// blob rather than a table per event type, per the app's existing pattern.
/// </summary>
public class AuditLogEntry
{
    public int Id { get; set; }
    public int? AppUserId { get; set; }
    public required string Action { get; set; } // e.g. "File.Create", "Permission.Grant"
    public string? TargetPath { get; set; } // human-readable object name/path
    public string? Details { get; set; }
    public DateTimeOffset Timestamp { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Coarse category of the affected object - "Document", "Folder", "User",
    /// "Permission", "Setting", "Auth". Null for events with no single affected object.</summary>
    public string? ObjectType { get; set; }

    /// <summary>Id of the affected object within its own table (PageMetadata.Id, AppUser.Id,
    /// FolderPermission.Id, ...), when ObjectType identifies one that has a stable id.</summary>
    public int? ObjectId { get; set; }

    public string? IpAddress { get; set; }

    /// <summary>For document-modification events, the DocumentVersion this event's "after"
    /// state corresponds to - lets the activity UI jump straight to a before/after diff.</summary>
    public int? RelatedVersionId { get; set; }

    /// <summary>Number of underlying occurrences this row represents. 1 for a normal event;
    /// greater than 1 when consecutive similar events (e.g. repeated rejected auth tokens from
    /// the same IP) were coalesced into one row instead of flooding the log - see
    /// AuditLogService.LogGroupedAsync.</summary>
    public int OccurrenceCount { get; set; } = 1;

    /// <summary>End of the occurrence window for a grouped event (Timestamp is the start).
    /// Null for a non-grouped (OccurrenceCount == 1) event.</summary>
    public DateTimeOffset? LastOccurredAtUtc { get; set; }
}
