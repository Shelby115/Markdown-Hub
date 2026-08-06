namespace MarkdownHub.Api.Controllers.Admin;

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
