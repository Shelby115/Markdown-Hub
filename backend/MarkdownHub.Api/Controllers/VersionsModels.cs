namespace MarkdownHub.Api.Controllers;

public record VersionSummaryDto(int Id, int DocumentId, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    bool IsOpen, string VersionType, int? UserId, string? Username, string RelativePath);

public record VersionDetailDto(int Id, int DocumentId, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    bool IsOpen, string VersionType, int? UserId, string? Username, string RelativePath, string Content);

public record DocumentHistoryDto(int DocumentId, string RelativePath, bool IsDeleted, IReadOnlyList<VersionSummaryDto> Versions);

public record CompareResultDto(VersionDetailDto From, VersionDetailDto To);

public record DeletedDocumentDto(int DocumentId, string RelativePath, string PageName,
    DateTimeOffset? DeletedAtUtc, int? DeletedByUserId, string? DeletedByUsername, int? LatestVersionId);
