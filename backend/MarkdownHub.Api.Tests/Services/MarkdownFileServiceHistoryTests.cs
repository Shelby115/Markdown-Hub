using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

/// <summary>
/// Covers the history-preservation guarantees MarkdownFileService is now responsible for:
/// renames/moves keep the same stable document Id (and therefore version history), deletes are
/// soft (recoverable) rather than destroying the row, and restoring reactivates the same row
/// rather than creating an unrelated new one. Uses a real temp SQLite file (not InMemory) for
/// the search connection since SearchIndexService manages its own raw ADO.NET connection
/// independent of AppDbContext's provider - a `:memory:` connection string would open a fresh,
/// schema-less database on every call. IAsyncLifetime (rather than the constructor) is needed
/// to await EnsureSchemaAsync before any test runs, the same schema setup DatabaseMigrations
/// performs for real at startup.
/// </summary>
public class MarkdownFileServiceHistoryTests : IAsyncLifetime
{
    private readonly string _hubRoot = Directory.CreateTempSubdirectory("markdown-hub-tests-hub-").FullName;
    private readonly string _searchDbPath = Path.Combine(Path.GetTempPath(), $"markdown-hub-tests-search-{Guid.NewGuid():N}.db");
    private AppDbContext _db = null!;
    private VersionService _versions = null!;
    private MarkdownFileService _sut = null!;

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hub:MarkdownRoot"] = _hubRoot,
                ["ConnectionStrings:Default"] = $"Data Source={_searchDbPath}",
            })
            .Build();
        var hub = new HubPathService(config);
        var search = new SearchIndexService(config);
        await search.EnsureSchemaAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _versions = new VersionService(_db);
        _sut = new MarkdownFileService(hub, _db, search, _versions);
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        try { Directory.Delete(_hubRoot, recursive: true); } catch { /* best effort */ }
        try { File.Delete(_searchDbPath); } catch { /* best effort */ }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task RenameAsync_PreservesTheDocumentsStableId()
    {
        await _sut.WriteAsync("Session 5.md", "# Session 5", null, actingUserId: 1);
        var before = await _db.Pages.SingleAsync(p => p.RelativePath == "Session 5.md");

        await _sut.RenameAsync("Session 5.md", "Session 6.md");

        var after = await _db.Pages.SingleAsync(p => !p.IsDeleted);
        Assert.Equal(before.Id, after.Id);
        Assert.Equal("Session 6.md", after.RelativePath);
        Assert.Equal("Session 6", after.PageName);
    }

    [Fact]
    public async Task RenameAsync_ToADifferentFolder_StillPreservesHistory()
    {
        var written = await _sut.WriteAsync("Session 5.md", "# Session 5", null, actingUserId: 1);
        var documentId = written.VersionResult.Version!.DocumentId;

        await _sut.RenameAsync("Session 5.md", "Campaign/Session 5.md");

        var history = await _versions.GetHistoryAsync(documentId);
        Assert.Single(history);
    }

    [Fact]
    public async Task DeleteAsync_SoftDeletes_PreservingTheRowAndItsHistory()
    {
        var written = await _sut.WriteAsync("Notes.md", "Content", null, actingUserId: 1);
        var documentId = written.VersionResult.Version!.DocumentId;

        await _sut.DeleteAsync("Notes.md", actingUserId: 1);

        Assert.False(File.Exists(Path.Combine(_hubRoot, "Notes.md")));
        var meta = await _db.Pages.SingleAsync(p => p.Id == documentId);
        Assert.True(meta.IsDeleted);
        Assert.NotNull(meta.DeletedAtUtc);
        Assert.Equal(1, meta.DeletedByAppUserId);
        Assert.NotEmpty(await _versions.GetHistoryAsync(documentId));
    }

    [Fact]
    public async Task DeleteAsync_ClosesAnyOpenVersion()
    {
        var written = await _sut.WriteAsync("Notes.md", "Content", null, actingUserId: 1);
        var documentId = written.VersionResult.Version!.DocumentId;

        await _sut.DeleteAsync("Notes.md", actingUserId: 1);

        var history = await _versions.GetHistoryAsync(documentId);
        Assert.All(history, v => Assert.False(v.IsOpen));
    }

    [Fact]
    public async Task WriteRestoredContentAsync_OnADeletedDocument_RecreatesTheFileAndClearsIsDeleted()
    {
        var written = await _sut.WriteAsync("Notes.md", "Original content", null, actingUserId: 1);
        var documentId = written.VersionResult.Version!.DocumentId;
        await _sut.DeleteAsync("Notes.md", actingUserId: 1);
        var meta = await _db.Pages.SingleAsync(p => p.Id == documentId);

        await _sut.WriteRestoredContentAsync(meta, "Original content");

        var restoredPath = Path.Combine(_hubRoot, "Notes.md");
        Assert.True(File.Exists(restoredPath));
        Assert.Equal("Original content", await File.ReadAllTextAsync(restoredPath));
        var reloaded = await _db.Pages.SingleAsync(p => p.Id == documentId);
        Assert.False(reloaded.IsDeleted);
        Assert.Null(reloaded.DeletedAtUtc);
    }

    [Fact]
    public async Task WriteRestoredContentAsync_PathNowTakenByADifferentActiveDocument_ThrowsConflict()
    {
        var written = await _sut.WriteAsync("Notes.md", "Original content", null, actingUserId: 1);
        var documentId = written.VersionResult.Version!.DocumentId;
        await _sut.DeleteAsync("Notes.md", actingUserId: 1);
        var deletedMeta = await _db.Pages.SingleAsync(p => p.Id == documentId);

        // A different, unrelated document now occupies the same path.
        await _sut.WriteAsync("Notes.md", "Someone else's new page", null, actingUserId: 2);

        await Assert.ThrowsAsync<RestorePathConflictException>(
            () => _sut.WriteRestoredContentAsync(deletedMeta, "Original content"));
    }

    [Fact]
    public async Task WriteAsync_NewPageAtAPathThatHadADeletedDocument_GetsItsOwnDistinctId()
    {
        var first = await _sut.WriteAsync("Notes.md", "First document", null, actingUserId: 1);
        var firstId = first.VersionResult.Version!.DocumentId;
        await _sut.DeleteAsync("Notes.md", actingUserId: 1);

        var second = await _sut.WriteAsync("Notes.md", "Second, unrelated document", null, actingUserId: 1);

        Assert.NotEqual(firstId, second.VersionResult.Version!.DocumentId);
        Assert.True(second.VersionResult.IsNewDocument);
    }
}
