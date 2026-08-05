namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// Record of a saved conflict when two editors modified the same file concurrently.
/// The original file is left untouched; the losing write is saved alongside it
/// (e.g. "Notes.conflict.2026-08-03T12-00-00Z.md") for manual review/merge.
/// </summary>
public class ConflictFile
{
    public int Id { get; set; }
    public required string OriginalRelativePath { get; set; }
    public required string ConflictRelativePath { get; set; }
    public int CreatedByAppUserId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool Resolved { get; set; }
}
