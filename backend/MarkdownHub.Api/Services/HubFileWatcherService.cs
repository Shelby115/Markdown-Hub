using System.Collections.Concurrent;
using MarkdownHub.Api.Data;
using Microsoft.EntityFrameworkCore;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Watches the hub directory for changes made outside the application (e.g. via
/// git, rsync, another markdown app, a text editor) and keeps the search index / page
/// metadata in sync. Debounces bursts of events (e.g. a git checkout of many files).
/// </summary>
public class HubFileWatcherService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly HubPathService _hub;
    private readonly ILogger<HubFileWatcherService> _logger;
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly ConcurrentQueue<(string OldFullPath, string NewFullPath)> _pendingRenames = new();
    private FileSystemWatcher? _watcher;

    public HubFileWatcherService(IServiceScopeFactory scopeFactory, HubPathService hub, ILogger<HubFileWatcherService> logger)
    {
        _scopeFactory = scopeFactory;
        _hub = hub;
        _logger = logger;
    }

    public override async Task StartAsync(CancellationToken cancellationToken)
    {
        _watcher = new FileSystemWatcher(_hub.Root)
        {
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.DirectoryName,
            Filter = "*.md"
        };
        _watcher.Changed += (_, e) => _pending[e.FullPath] = 1;
        _watcher.Created += (_, e) => _pending[e.FullPath] = 1;
        // Tracked as an explicit old/new pair (not just two independent dirty paths) so the
        // debounce loop can update the existing PageMetadata row in place - preserving its
        // stable Id (and therefore its version/activity history) - instead of the generic
        // created/deleted handling seeing this as an unrelated delete-then-create and
        // destroying that history. See MarkdownFileService.RenameAsync for the same fix on the
        // in-app rename path.
        _watcher.Renamed += (_, e) => _pendingRenames.Enqueue((e.OldFullPath, e.FullPath));
        _watcher.Deleted += (_, e) => _pending[e.FullPath] = 1;
        _watcher.EnableRaisingEvents = true;

        await base.StartAsync(cancellationToken);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // One-time reconciliation on startup: the watcher above only sees *live* filesystem
        // events, so any content already on disk when the container starts (e.g. a hub
        // bind-mounted from an existing markdown notes folder) would otherwise never be indexed.
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var fileService = scope.ServiceProvider.GetRequiredService<MarkdownFileService>();
            foreach (var absolutePath in Directory.EnumerateFiles(_hub.Root, "*.md", SearchOption.AllDirectories))
            {
                try
                {
                    await fileService.IndexPageAsync(_hub.ToRelative(absolutePath), null, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to index {Path} during startup reconciliation", absolutePath);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Startup search index reconciliation failed");
        }

        // Debounce: flush any pending changes every 2 seconds rather than reindexing
        // on every single filesystem event (a git checkout can fire hundreds at once).
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(2), stoppingToken);
            if (_pending.IsEmpty && _pendingRenames.IsEmpty) continue;

            var renames = new List<(string OldFullPath, string NewFullPath)>();
            while (_pendingRenames.TryDequeue(out var rename)) renames.Add(rename);

            var batch = _pending.Keys.ToList();
            foreach (var path in batch) _pending.TryRemove(path, out _);

            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var fileService = scope.ServiceProvider.GetRequiredService<MarkdownFileService>();
            var versions = scope.ServiceProvider.GetRequiredService<VersionService>();
            var search = scope.ServiceProvider.GetRequiredService<SearchIndexService>();

            foreach (var (oldFullPath, newFullPath) in renames)
            {
                try
                {
                    var oldRelative = _hub.ToRelative(oldFullPath);
                    var newRelative = _hub.ToRelative(newFullPath);
                    // A rename's own Created/Deleted-shaped side effects would otherwise be
                    // double-processed by the generic handling below.
                    batch.Remove(oldFullPath);
                    batch.Remove(newFullPath);

                    var meta = await db.Pages.FirstOrDefaultAsync(p => p.RelativePath == oldRelative && !p.IsDeleted, stoppingToken);
                    if (meta is not null)
                    {
                        meta.RelativePath = newRelative;
                        meta.PageName = Path.GetFileNameWithoutExtension(newRelative);
                        await db.SaveChangesAsync(stoppingToken);
                    }
                    await search.RemoveAsync(oldRelative, stoppingToken);
                    if (File.Exists(newFullPath))
                        await fileService.IndexPageAsync(newRelative, null, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to reindex rename {Old} -> {New} after external change", oldFullPath, newFullPath);
                }
            }

            foreach (var absolutePath in batch)
            {
                try
                {
                    var relative = _hub.ToRelative(absolutePath);
                    if (File.Exists(absolutePath))
                    {
                        await fileService.IndexPageAsync(relative, null, stoppingToken);
                    }
                    else
                    {
                        // Soft-delete, matching the in-app delete path - an externally-deleted
                        // file's version history must stay recoverable too, not just one deleted
                        // through the UI.
                        var meta = await db.Pages.FirstOrDefaultAsync(p => p.RelativePath == relative && !p.IsDeleted, stoppingToken);
                        if (meta is not null)
                        {
                            await versions.CloseOpenVersionAsync(meta.Id, stoppingToken);
                            meta.IsDeleted = true;
                            meta.DeletedAtUtc = DateTimeOffset.UtcNow;
                            await db.SaveChangesAsync(stoppingToken);
                        }
                        await search.RemoveAsync(relative, stoppingToken);
                    }
                }
                catch (Exception ex)
                {
                    // Never let one bad file abort the whole reindex batch.
                    _logger.LogWarning(ex, "Failed to reindex {Path} after external change", absolutePath);
                }
            }
        }
    }

    public override void Dispose()
    {
        _watcher?.Dispose();
        base.Dispose();
    }
}
