namespace MarkdownHub.Api.Data.Entities.Admin;

public class BackupRecord
{
    public int Id { get; set; }
    public required string FileName { get; set; }
    public long SizeBytes { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public bool TriggeredManually { get; set; }
}
