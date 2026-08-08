using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;
using MarkdownHub.Api.Tests.Controllers;

namespace MarkdownHub.Api.Tests.Services;

public class GenerationPoolServiceTests : IDisposable
{
    private const string Instructions = "- One brief interactible.\n- Max words: 20\n";

    private readonly AppDbContext _db;
    private readonly FakeAiService _ai = new();
    private readonly GenerationPoolService _sut;

    public GenerationPoolServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _sut = new GenerationPoolService(_db, _ai);
    }

    public void Dispose() => _db.Dispose();

    private Task<GenerationPool> CreatePoolAsync(int targetCount = 5, bool enabled = true) =>
        _sut.CreatePoolAsync("Interactible", Instructions, targetCount, enabled);

    [Fact]
    public async Task GetSettingsAsync_NothingSaved_ReturnsDefaults()
    {
        var settings = await _sut.GetSettingsAsync();

        Assert.False(settings.Paused);
        Assert.Null(settings.WindowStartUtc);
        Assert.Equal(60, settings.IntervalSeconds);
        Assert.Equal(90, settings.UsedEntryRetentionDays);
    }

    [Fact]
    public async Task SaveSettingsAsync_RoundTrips()
    {
        await _sut.SaveSettingsAsync(new GenerationPoolSettings(true, "22:00", "06:00", 120, 30));

        var settings = await _sut.GetSettingsAsync();

        Assert.True(settings.Paused);
        Assert.Equal("22:00", settings.WindowStartUtc);
        Assert.Equal("06:00", settings.WindowEndUtc);
        Assert.Equal(120, settings.IntervalSeconds);
        Assert.Equal(30, settings.UsedEntryRetentionDays);
    }

    [Theory]
    [InlineData("25:00", null, 60)]
    [InlineData(null, null, 5)]
    [InlineData(null, null, 999999)]
    public async Task SaveSettingsAsync_RejectsOutOfRangeValues(string? start, string? end, int interval)
    {
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.SaveSettingsAsync(new GenerationPoolSettings(false, start, end, interval, 30)));
    }

    [Fact]
    public async Task CreatePoolAsync_RejectsDuplicateAndInvalidNames()
    {
        await CreatePoolAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreatePoolAsync("Interactible", "", 5, true));
        await Assert.ThrowsAsync<ArgumentException>(() => _sut.CreatePoolAsync("Bad: Name", "", 5, true));
    }

    [Fact]
    public async Task GenerateEntryAsync_ValidReply_IsStoredAsReady()
    {
        var pool = await CreatePoolAsync();
        _ai.Respond = (_, _) => "A rusted lantern hangs here.";

        var entry = await _sut.GenerateEntryAsync(pool);

        Assert.NotNull(entry);
        Assert.Equal("A rusted lantern hangs here.", entry!.Content);
        Assert.Equal(GenerationPoolEntryStatus.Ready, entry.Status);
        Assert.Equal(1, await _sut.CountReadyAsync(pool.Id));
    }

    [Fact]
    public async Task GenerateEntryAsync_RepeatedContent_IsNotStoredTwice()
    {
        var pool = await CreatePoolAsync();
        _ai.Respond = (_, _) => "A rusted lantern hangs here.";

        await _sut.GenerateEntryAsync(pool);
        // Same text, different whitespace and casing - the hash normalizes both away.
        _ai.Respond = (_, _) => "a rusted  lantern hangs here.";

        Assert.Null(await _sut.GenerateEntryAsync(pool));
        Assert.Equal(1, await _sut.CountReadyAsync(pool.Id));
    }

    [Fact]
    public async Task GenerateEntryAsync_StillInvalidAfterRetry_IsDroppedRatherThanPooled()
    {
        var pool = await CreatePoolAsync();
        _ai.Respond = (_, _) => "## A heading, which the rules forbid.";

        Assert.Null(await _sut.GenerateEntryAsync(pool));
        Assert.Equal(0, await _sut.CountReadyAsync(pool.Id));
    }

    [Fact]
    public async Task GenerateEntryAsync_PromptListsWhatThePoolAlreadyHas()
    {
        var pool = await CreatePoolAsync();
        _ai.Respond = (_, _) => "A rusted lantern hangs here.";
        await _sut.GenerateEntryAsync(pool);

        _ai.Respond = (_, _) => "A cracked bell sits in the corner.";
        await _sut.GenerateEntryAsync(pool);

        Assert.Contains("A rusted lantern hangs here.", _ai.LastUserPrompt);
        Assert.Contains("ALREADY WRITTEN", _ai.LastUserPrompt);
    }

    [Fact]
    public async Task TakeAsync_HandsOutEachEntryOnlyOnce_ThenReturnsNullWhenEmpty()
    {
        var pool = await CreatePoolAsync();
        _ai.Respond = (_, _) => "A rusted lantern hangs here.";
        await _sut.GenerateEntryAsync(pool);
        _ai.Respond = (_, _) => "A cracked bell sits in the corner.";
        await _sut.GenerateEntryAsync(pool);

        var first = await _sut.TakeAsync("Interactible");
        var second = await _sut.TakeAsync("Interactible");

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.NotEqual(first!.Id, second!.Id);
        Assert.Null(await _sut.TakeAsync("Interactible"));
        Assert.Equal(0, await _sut.CountReadyAsync(pool.Id));
    }

    [Fact]
    public async Task TakeAsync_UnknownPool_ReturnsNull()
    {
        Assert.Null(await _sut.TakeAsync("Nonexistent"));
    }

    [Fact]
    public async Task ForgetAsync_StopsTheEntryComingBack_EvenFromANewGeneration()
    {
        var pool = await CreatePoolAsync();
        _ai.Respond = (_, _) => "A rusted lantern hangs here.";
        var entry = await _sut.GenerateEntryAsync(pool);

        Assert.True(await _sut.ForgetAsync(entry!.Id));

        Assert.Null(await _sut.TakeAsync("Interactible"));
        Assert.Null(await _sut.GenerateEntryAsync(pool)); // model offers the same text again
        Assert.Equal(0, await _sut.CountReadyAsync(pool.Id));
    }

    [Fact]
    public async Task RecordUsedAsync_BlocksThatContentFromBeingGeneratedLater()
    {
        var pool = await CreatePoolAsync();
        await _sut.RecordUsedAsync("Interactible", "A rusted lantern hangs here.");
        _ai.Respond = (_, _) => "A rusted lantern hangs here.";

        Assert.Null(await _sut.GenerateEntryAsync(pool));
    }

    [Fact]
    public async Task CleanupUsedEntriesAsync_RemovesOldUsedEntries_ButNeverForgottenOnes()
    {
        var pool = await CreatePoolAsync();
        var old = DateTimeOffset.UtcNow.AddDays(-100);
        _db.GenerationPoolEntries.AddRange(
            new GenerationPoolEntry { PoolId = pool.Id, Content = "used", ContentHash = "A", Status = GenerationPoolEntryStatus.Used, SpentAtUtc = old },
            new GenerationPoolEntry { PoolId = pool.Id, Content = "recent", ContentHash = "B", Status = GenerationPoolEntryStatus.Used, SpentAtUtc = DateTimeOffset.UtcNow },
            new GenerationPoolEntry { PoolId = pool.Id, Content = "forgotten", ContentHash = "C", Status = GenerationPoolEntryStatus.Forgotten, SpentAtUtc = old },
            new GenerationPoolEntry { PoolId = pool.Id, Content = "ready", ContentHash = "D", Status = GenerationPoolEntryStatus.Ready, CreatedAtUtc = old });
        await _db.SaveChangesAsync();

        Assert.Equal(1, await _sut.CleanupUsedEntriesAsync(90));

        var remaining = _db.GenerationPoolEntries.Select(e => e.Content).ToList();
        Assert.Equal(["recent", "forgotten", "ready"], remaining);
    }

    [Fact]
    public async Task FillOnceAsync_AddsOneEntryPerEnabledPoolBelowTarget()
    {
        await CreatePoolAsync(targetCount: 5);
        await _sut.CreatePoolAsync("NPC Name", "- One name.\n", 5, enabled: true);
        await _sut.CreatePoolAsync("Rumour", "- One rumour.\n", 5, enabled: false);
        var replies = 0;
        _ai.Respond = (_, _) => $"Entry number {++replies}.";

        Assert.Equal(2, await _sut.FillOnceAsync());

        Assert.Equal(1, await _sut.CountReadyAsync((await _sut.FindPoolAsync("Interactible"))!.Id));
        Assert.Equal(0, await _sut.CountReadyAsync((await _sut.FindPoolAsync("Rumour"))!.Id));
    }

    [Fact]
    public async Task FillOnceAsync_PoolAtItsTarget_IsLeftAlone()
    {
        var pool = await CreatePoolAsync(targetCount: 1);
        _ai.Respond = (_, _) => "A rusted lantern hangs here.";
        await _sut.GenerateEntryAsync(pool);

        Assert.Equal(0, await _sut.FillOnceAsync());
    }

    [Fact]
    public async Task DeletePoolAsync_RemovesItsEntriesToo()
    {
        var pool = await CreatePoolAsync();
        _ai.Respond = (_, _) => "A rusted lantern hangs here.";
        await _sut.GenerateEntryAsync(pool);

        await _sut.DeletePoolAsync(pool);

        Assert.Empty(_db.GenerationPools);
        Assert.Empty(_db.GenerationPoolEntries);
    }
}
