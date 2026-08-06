namespace MarkdownHub.Api.Controllers.Admin;

public record SetHistorySettingsRequest(int VersionRetentionDays, int ActivityRetentionDays, int ActivityDefaultDays);
