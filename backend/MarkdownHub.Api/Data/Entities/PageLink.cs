namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// A directional [[wiki link]] relationship between two pages, used to compute backlinks.
/// TargetPageId is null when the target page does not exist yet (unresolved link).
/// </summary>
public class PageLink
{
    public int Id { get; set; }

    public int SourcePageId { get; set; }
    public PageMetadata? SourcePage { get; set; }

    public int? TargetPageId { get; set; }
    public PageMetadata? TargetPage { get; set; }

    /// <summary>Raw target as written, e.g. "Folder/PageName#Anchor".</summary>
    public required string RawTarget { get; set; }
}
