namespace MarkdownHub.Api.Services;

/// <summary>
/// Keeps enabled generation pools topped up in the background, so a template placeholder backed by
/// a pool is served from the database instead of waiting on the model. Deliberately unhurried: one
/// entry per pool per tick, and nothing at all while paused or outside the configured window - the
/// point is to use idle time, not to compete with someone actually editing.
/// </summary>
public class PoolFillHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PoolFillHostedService> _logger;

    public PoolFillHostedService(IServiceScopeFactory scopeFactory, ILogger<PoolFillHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            var interval = await RunOnceAsync(stoppingToken);
            try
            {
                await Task.Delay(interval, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>Runs one pass and returns how long to wait before the next one - settings are read
    /// fresh every tick, so pausing or changing the interval takes effect without a restart.</summary>
    private async Task<TimeSpan> RunOnceAsync(CancellationToken ct)
    {
        var interval = TimeSpan.FromSeconds(60);
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var pools = scope.ServiceProvider.GetRequiredService<GenerationPoolService>();

            var settings = await pools.GetSettingsAsync(ct);
            interval = TimeSpan.FromSeconds(settings.IntervalSeconds);
            if (!settings.IsAllowedAt(DateTimeOffset.UtcNow))
            {
                return interval;
            }

            var added = await pools.FillOnceAsync(ct);
            if (added > 0)
            {
                _logger.LogInformation("Pool generation: added {Added} entry/entries", added);
            }
        }
        catch (OperationCanceledException)
        {
            // Shutting down.
        }
        catch (Exception ex)
        {
            // Includes AiServiceException when Ollama is down - keep ticking and try again later.
            _logger.LogError(ex, "Pool generation pass failed");
        }

        return interval;
    }
}
