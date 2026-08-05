namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// One row per Markdown file. Rebuildable from the filesystem at any time -
/// this is a cache/index, never the source of truth for content.
/// </summary>
public class PageMetadata
{
    public int Id { get; set; }

    /// <summary>Relative path from MarkdownRoot, e.g. "Projects/Ideas.md".</summary>
    public required string RelativePath { get; set; }

    /// <summary>File name without extension - used for [[WikiLink]] resolution.</summary>
    public required string PageName { get; set; }

    public DateTimeOffset LastModifiedUtc { get; set; }
    public long SizeBytes { get; set; }
    public bool IsPublished { get; set; }

    /// <summary>Stable slug used for the public published URL.</summary>
    public string? PublishSlug { get; set; }

    /// <summary>Marks this page as available as a starting point when creating a new page -
    /// can live anywhere in the hub, not restricted to a special folder.</summary>
    public bool IsTemplate { get; set; }

    /// <summary>Soft-delete flag. Deleting a page never removes this row - Id is the stable
    /// "logical document ID" that version history and activity events are keyed on, and a
    /// hard delete would destroy that history and make the deletion unrecoverable. A deleted
    /// page is simply hidden from the tree/search/wikilink resolution until restored or swept
    /// up by retention cleanup once its history has aged out.</summary>
    public bool IsDeleted { get; set; }
    public DateTimeOffset? DeletedAtUtc { get; set; }
    public int? DeletedByAppUserId { get; set; }

    public ICollection<PageLink> OutgoingLinks { get; set; } = new List<PageLink>();
    public ICollection<PageLink> IncomingLinks { get; set; } = new List<PageLink>();
}
