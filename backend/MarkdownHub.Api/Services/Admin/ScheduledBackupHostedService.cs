namespace MarkdownHub.Api.Services;

/// <summary>Runs BackupService on the configured cron-like schedule.</summary>
public class ScheduledBackupHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ScheduledBackupHostedService> _logger;

    public ScheduledBackupHostedService(IServiceScopeFactory scopeFactory, ILogger<ScheduledBackupHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Simplified fixed-interval scheduler (daily). Swap in a proper cron
        // library (e.g. Cronos/Quartz) if sub-day schedules are needed.
        while (!stoppingToken.IsCancellationRequested)
        {
            var now = DateTimeOffset.UtcNow;
            var next = now.Date.AddDays(1).AddHours(3); // 03:00 UTC daily
            await Task.Delay(next - now, stoppingToken);

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var backup = scope.ServiceProvider.GetRequiredService<BackupService>();
                await backup.RunBackupAsync(manual: false, stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Scheduled backup failed");
            }
        }
    }
}
