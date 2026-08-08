namespace MarkdownHub.Api.Services;

/// <summary>
/// Which pool the background generator is writing an entry for right now, if any. A singleton so
/// the admin page can show live progress: a model call takes long enough that without this, a
/// filling pool is indistinguishable from a stalled one.
/// </summary>
public class PoolActivityTracker
{
    private volatile string? _currentPoolName;

    public string? CurrentPoolName => _currentPoolName;

    public void Start(string poolName) => _currentPoolName = poolName;

    public void Finish() => _currentPoolName = null;
}
