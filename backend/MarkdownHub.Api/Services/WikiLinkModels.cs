namespace MarkdownHub.Api.Services;

public enum WikiLinkKind { Link, Embed }

public record ParsedWikiLink(
    WikiLinkKind Kind,
    string Target,      // e.g. "Folder/PageName"
    string? Anchor,      // e.g. "Section Heading"
    string? DisplayText, // explicit |LinkText override, if any
    string RawMatch
);
