using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class VersionServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly VersionService _sut;

    public VersionServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _sut = new VersionService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task MaybeRecordVersionAsync_FirstSaveEver_AlwaysCreatesAVersion_EvenWithEmptyContent()
    {
        var result = await _sut.MaybeRecordVersionAsync(1, "Notes.md", "", userId: 5);

        Assert.True(result.Changed);
        Assert.True(result.IsNewDocument);
        Assert.NotNull(result.Version);
        Assert.Equal("", result.Version!.Content);
        Assert.True(result.Version.IsOpen);
    }

    [Fact]
    public async Task MaybeRecordVersionAsync_SecondSaveWithIdenticalContent_DoesNotCreateAnotherVersion()
    {
        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.", userId: 5);

        var result = await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.", userId: 5);

        Assert.False(result.Changed);
        Assert.Null(result.Version);
        Assert.Single(await _db.DocumentVersions.Where(v => v.DocumentId == 1).ToListAsync());
    }

    [Fact]
    public async Task MaybeRecordVersionAsync_MeaningfulChange_CreatesANewVersion()
    {
        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.", userId: 5);

        var result = await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.\nMore text.", userId: 5);

        Assert.True(result.Changed);
        Assert.False(result.IsNewDocument);
        Assert.Equal("Hello world.\nMore text.", result.Version!.Content);
    }

    /// <summary>
    /// Add text, then revert back to the original within the same editing burst. The open
    /// version is coalesced/updated in place throughout, so there's still only ever one row -
    /// but since this document has never had a *closed* baseline to compare the final revert
    /// against, this last save is (correctly) still treated as a content change, just one that
    /// happens to land back on the original text. See the dedicated "...AfterBaselineWasClosed"
    /// test below for the case where a real closed baseline exists and gets detected/discarded.
    /// </summary>
    [Fact]
    public async Task MaybeRecordVersionAsync_EditThenRevertWithinTheSameOpenBurst_StaysAsOneVersionWithFinalContent()
    {
        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.", userId: 5);
        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.\nThis is additional text.", userId: 5);

        var result = await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.", userId: 5);

        Assert.True(result.Changed);
        var all = await _db.DocumentVersions.Where(v => v.DocumentId == 1).ToListAsync();
        var version = Assert.Single(all);
        Assert.Equal("Hello world.", version.Content);
    }

    /// <summary>
    /// The exact scenario from Activity-And-History.md section 1.1, with a genuine settled
    /// (closed) baseline already in place before the edit burst starts: add text, then revert
    /// back to the original. The net effect versus the baseline is zero, so the speculative open
    /// version created for the burst must be discarded entirely - not left behind as a version
    /// identical to the baseline right before it.
    /// </summary>
    [Fact]
    public async Task MaybeRecordVersionAsync_EditThenRevertAfterBaselineWasClosed_DiscardsTheSpeculativeVersion()
    {
        _db.DocumentVersions.Add(new DocumentVersion { DocumentId = 1, Content = "Hello world.", RelativePath = "Notes.md", IsOpen = false });
        await _db.SaveChangesAsync();

        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.\nThis is additional text.", userId: 5);
        var result = await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.", userId: 5);

        Assert.False(result.Changed);
        var all = await _db.DocumentVersions.Where(v => v.DocumentId == 1).ToListAsync();
        var version = Assert.Single(all); // the speculative open version is gone; only the original baseline remains
        Assert.False(version.IsOpen);
        Assert.Equal("Hello world.", version.Content);
    }

    [Fact]
    public async Task MaybeRecordVersionAsync_ResavingTheSameContentTwice_IsATrueNoOp()
    {
        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.\nDraft.", userId: 5);

        var result = await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.\nDraft.", userId: 5);

        Assert.False(result.Changed);
        Assert.Single(await _db.DocumentVersions.Where(v => v.DocumentId == 1).ToListAsync());
    }

    [Fact]
    public async Task MaybeRecordVersionAsync_RapidSuccessiveEdits_CoalesceIntoOneOpenVersionOnTopOfTheBaseline()
    {
        _db.DocumentVersions.Add(new DocumentVersion { DocumentId = 1, Content = "Hello world.", RelativePath = "Notes.md", IsOpen = false });
        await _db.SaveChangesAsync();

        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.\nDraft one.", userId: 5);
        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.\nDraft two.", userId: 5);
        var result = await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.\nFinal text.", userId: 5);

        Assert.True(result.Changed);
        var versions = await _db.DocumentVersions.Where(v => v.DocumentId == 1).ToListAsync();
        // The closed baseline plus one coalesced open version - not three separate rows for
        // three differing saves.
        Assert.Equal(2, versions.Count);
        Assert.Equal("Hello world.\nFinal text.", versions.Single(v => v.IsOpen).Content);
    }

    [Fact]
    public async Task MaybeRecordVersionAsync_EditAfterTheCoalesceWindowHasElapsed_StartsANewVersion()
    {
        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.", userId: 5);
        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.\nDraft.", userId: 5);

        // Simulate the coalescing window (10 minutes) having elapsed since the open version was
        // last touched, without needing to actually wait in real time.
        var open = await _db.DocumentVersions.SingleAsync(v => v.DocumentId == 1 && v.IsOpen);
        open.UpdatedAtUtc = DateTimeOffset.UtcNow.AddMinutes(-15);
        await _db.SaveChangesAsync();

        var result = await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Hello world.\nDraft.\nMore, later.", userId: 6);

        Assert.True(result.Changed);
        var versions = await _db.DocumentVersions.Where(v => v.DocumentId == 1).OrderBy(v => v.Id).ToListAsync();
        Assert.Equal(2, versions.Count);
        Assert.False(versions[0].IsOpen); // the stale draft is now closed, permanent history
        Assert.Equal("Hello world.\nDraft.", versions[0].Content);
        Assert.True(versions[1].IsOpen);
        Assert.Equal("Hello world.\nDraft.\nMore, later.", versions[1].Content);
    }

    [Fact]
    public async Task CreateRestoreVersionAsync_ProducesANewClosedVersion_WithoutRemovingAnyExistingOne()
    {
        _db.DocumentVersions.Add(new DocumentVersion { DocumentId = 1, Content = "Version 1", RelativePath = "Notes.md", IsOpen = false });
        _db.DocumentVersions.Add(new DocumentVersion { DocumentId = 1, Content = "Version 2", RelativePath = "Notes.md", IsOpen = false });
        await _db.SaveChangesAsync();

        var restored = await _sut.CreateRestoreVersionAsync(1, "Notes.md", "Version 1", userId: 5);

        Assert.Equal("Version 1", restored.Content);
        Assert.False(restored.IsOpen);
        Assert.Equal(DocumentVersionType.Restore, restored.VersionType);

        var all = await _db.DocumentVersions.Where(v => v.DocumentId == 1).OrderBy(v => v.Id).ToListAsync();
        Assert.Equal(3, all.Count); // original + edit + restore - nothing removed
        Assert.Equal("Version 1", all[0].Content);
        Assert.Equal("Version 2", all[1].Content);
        Assert.Equal("Version 1", all[2].Content);
    }

    [Fact]
    public async Task CreateRestoreVersionAsync_ClosesAnyOpenVersionFirst()
    {
        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "Version 1", userId: 5);
        await _sut.MaybeRecordVersionAsync(1, "Notes.md", "In-progress edit", userId: 5);

        await _sut.CreateRestoreVersionAsync(1, "Notes.md", "Version 1", userId: 5);

        var openCount = await _db.DocumentVersions.CountAsync(v => v.DocumentId == 1 && v.IsOpen);
        Assert.Equal(0, openCount);
    }

    [Fact]
    public async Task CloseOpenVersionAsync_NoOpenVersion_DoesNothing()
    {
        await _sut.CloseOpenVersionAsync(999); // no versions exist for this document at all
        // Should not throw.
    }

    [Fact]
    public async Task CleanupExpiredVersionsAsync_RemovesOnlyVersionsOlderThanRetention()
    {
        _db.DocumentVersions.Add(new DocumentVersion { DocumentId = 1, Content = "old", RelativePath = "A.md", CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-10), IsOpen = false });
        _db.DocumentVersions.Add(new DocumentVersion { DocumentId = 1, Content = "recent", RelativePath = "A.md", CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1), IsOpen = false });
        await _db.SaveChangesAsync();

        var removed = await _sut.CleanupExpiredVersionsAsync(retentionDays: 3);

        Assert.Equal(1, removed);
        var remaining = await _db.DocumentVersions.Where(v => v.DocumentId == 1).ToListAsync();
        var version = Assert.Single(remaining);
        Assert.Equal("recent", version.Content);
    }

    [Fact]
    public async Task CleanupExpiredVersionsAsync_RunTwice_IsIdempotent()
    {
        _db.DocumentVersions.Add(new DocumentVersion { DocumentId = 1, Content = "old", RelativePath = "A.md", CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-10), IsOpen = false });
        await _db.SaveChangesAsync();

        await _sut.CleanupExpiredVersionsAsync(retentionDays: 3);
        var secondRun = await _sut.CleanupExpiredVersionsAsync(retentionDays: 3);

        Assert.Equal(0, secondRun);
    }
}
