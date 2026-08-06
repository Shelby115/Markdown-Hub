using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Services;

/// <summary>Outcome of evaluating a save against the version-coalescing rules.</summary>
public record VersionRecordResult(DocumentVersion? Version, bool Changed, bool IsNewDocument);
