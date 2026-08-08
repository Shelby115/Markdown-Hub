namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// One pre-generated piece of content in a <see cref="GenerationPool"/>. Rows are kept after
/// they've been handed out or rejected rather than deleted: <see cref="ContentHash"/> is what
/// stops the background generator from ever producing the same text again, so throwing the row
/// away would let a forgotten entry come straight back.
/// </summary>
public class GenerationPoolEntry
{
    public int Id { get; set; }

    public int PoolId { get; set; }

    public required string Content { get; set; }

    /// <summary>SHA-256 of the normalized content - unique per pool, so duplicates are never stored.</summary>
    public required string ContentHash { get; set; }

    public string Status { get; set; } = GenerationPoolEntryStatus.Ready;

    public DateTimeOffset CreatedAtUtc { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>When this entry left the Ready state - used, or forgotten.</summary>
    public DateTimeOffset? SpentAtUtc { get; set; }
}
