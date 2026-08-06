namespace MarkdownHub.Api.Services;

public record HistorySettings(int VersionRetentionDays, int ActivityRetentionDays, int ActivityDefaultDays);
