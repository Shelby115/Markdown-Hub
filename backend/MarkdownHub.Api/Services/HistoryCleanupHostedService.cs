namespace MarkdownHub.Api.Services;

/// <summary>
/// Enforces the Version History and Activity Log retention settings in the background - deletes
/// DocumentVersions/AuditLog rows older than their configured retention window. Never touches
/// current document state (the live file on disk, or its PageMetadata row) - only history.
/// Runs once shortly after startup (so short-lived containers still get a pass) and then daily,
/// mirroring ScheduledBackupHostedService's fixed-interval approach.
/// </summary>
public class HistoryCleanupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<HistoryCleanupHostedService> _logger;

    public HistoryCleanupHostedService(IServiceScopeFactory scopeFactory, ILogger<HistoryCleanupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RunOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var next = now.Date.AddDays(1).AddHours(4); // 04:00 UTC daily - after the 03:00 backup
            try
            {
                await Task.Delay(next - now, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            await RunOnceAsync(stoppingToken);
        }
    }

    private async Task RunOnceAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var settings = scope.ServiceProvider.GetRequiredService<HistorySettingsService>();
            var versions = scope.ServiceProvider.GetRequiredService<VersionService>();
            var audit = scope.ServiceProvider.GetRequiredService<AuditLogService>();

            var versionRetentionDays = await settings.GetVersionRetentionDaysAsync(ct);
            var activityRetentionDays = await settings.GetActivityRetentionDaysAsync(ct);

            var removedVersions = await versions.CleanupExpiredVersionsAsync(versionRetentionDays, ct);
            var removedActivity = await audit.CleanupExpiredAsync(activityRetentionDays, ct);

            _logger.LogInformation(
                "History cleanup: removed {Versions} expired version row(s) (retention {VersionDays}d), {Activity} expired activity row(s) (retention {ActivityDays}d)",
                removedVersions, versionRetentionDays, removedActivity, activityRetentionDays);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "History cleanup failed");
        }
    }
}
