namespace MarkdownHub.Api.Services;

public record PageDto(string RelativePath, string PageName, string Content, DateTimeOffset LastModifiedUtc, long SizeBytes);

public record WriteResult(PageDto Page, VersionRecordResult VersionResult);
