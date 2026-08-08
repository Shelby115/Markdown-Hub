namespace MarkdownHub.Api.Services;

/// <summary>
/// What the background generator is doing right now: which pool it's writing an entry for, and
/// when its next pass is due. A singleton so the admin page can show live progress - a model call
/// takes long enough that without this, a filling pool is indistinguishable from a stalled one,
/// and only the loop itself knows when it will next wake up.
/// </summary>
public class PoolActivityTracker
{
    private volatile string? _currentPoolName;
    private long _nextPassDueTicks;

    public string? CurrentPoolName => _currentPoolName;

    /// <summary>Null until the generator has scheduled its first pass.</summary>
    public DateTimeOffset? NextPassDueUtc
    {
        get
        {
            var ticks = Interlocked.Read(ref _nextPassDueTicks);
            return ticks == 0 ? null : new DateTimeOffset(ticks, TimeSpan.Zero);
        }
    }

    public void Start(string poolName) => _currentPoolName = poolName;

    public void Finish() => _currentPoolName = null;

    public void ScheduleNextPass(DateTimeOffset dueUtc) =>
        Interlocked.Exchange(ref _nextPassDueTicks, dueUtc.UtcTicks);
}
