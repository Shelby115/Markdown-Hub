namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// A complete snapshot of a document's Markdown content at some point in time. Versions belong
/// to the stable <see cref="PageMetadata.Id"/> ("DocumentId"), never to a path, so renames/moves
/// never break history.
///
/// While <see cref="IsOpen"/> is true, this row represents an in-progress coalescing edit burst:
/// further saves that still differ from the previous *closed* version update this same row in
/// place (see VersionService) instead of creating a new one, so a short editing session doesn't
/// produce dozens of near-duplicate versions. It becomes permanently immutable once closed.
/// </summary>
public class DocumentVersion
{
    public int Id { get; set; }

    /// <summary>The stable logical document ID - PageMetadata.Id. Deliberately not a foreign-key
    /// navigation property: PageMetadata rows are soft-deleted, never removed, so this is safe,
    /// but keeping it a plain int avoids EF cascade-delete semantics ever touching history.</summary>
    public int DocumentId { get; set; }

    public int? UserId { get; set; }

    public required string Content { get; set; }

    /// <summary>The document's path at the time this version was recorded - purely for display
    /// (e.g. "this version was saved when the page was named Session 5.md"). Not used to
    /// resolve anything; DocumentId is what history is keyed on.</summary>
    public required string RelativePath { get; set; }

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Last time this row was touched - equals CreatedAtUtc unless coalescing updates
    /// have kept extending it while IsOpen.</summary>
    public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    public bool IsOpen { get; set; } = true;

    public string VersionType { get; set; } = DocumentVersionType.Edit;
}
