using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Data.Entities.Admin;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class AuditLogServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuditLogService _sut;

    public AuditLogServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _sut = new AuditLogService(_db, new HttpContextAccessor { HttpContext = new DefaultHttpContext() });
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task LogAsync_CapturesTheRequestIpAddress()
    {
        var accessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext { Connection = { RemoteIpAddress = System.Net.IPAddress.Parse("203.0.113.5") } }
        };
        var sut = new AuditLogService(_db, accessor);

        await sut.LogAsync(1, "File.Modify", "Notes.md");

        var entry = await _db.AuditLog.SingleAsync();
        Assert.Equal("203.0.113.5", entry.IpAddress);
    }

    [Fact]
    public async Task LogEventAsync_SetsObjectTypeIdAndRelatedVersion()
    {
        await _sut.LogEventAsync(1, "File.Modify", "Notes.md", "Document", objectId: 42, relatedVersionId: 7);

        var entry = await _db.AuditLog.SingleAsync();
        Assert.Equal("Document", entry.ObjectType);
        Assert.Equal(42, entry.ObjectId);
        Assert.Equal(7, entry.RelatedVersionId);
    }

    [Fact]
    public async Task LogGroupedAsync_FirstOccurrence_CreatesANewRowWithCountOne()
    {
        await _sut.LogGroupedAsync("Auth.TokenRejected", null, "Auth", "203.0.113.9", "TokenExpired", TimeSpan.FromMinutes(5));

        var entry = await _db.AuditLog.SingleAsync();
        Assert.Equal(1, entry.OccurrenceCount);
        Assert.Null(entry.LastOccurredAtUtc);
    }

    [Fact]
    public async Task LogGroupedAsync_RepeatedWithinTheWindow_IncrementsTheSameRowInstead()
    {
        await _sut.LogGroupedAsync("Auth.TokenRejected", null, "Auth", "203.0.113.9", "TokenExpired", TimeSpan.FromMinutes(5));
        await _sut.LogGroupedAsync("Auth.TokenRejected", null, "Auth", "203.0.113.9", "TokenExpired", TimeSpan.FromMinutes(5));
        await _sut.LogGroupedAsync("Auth.TokenRejected", null, "Auth", "203.0.113.9", "TokenExpired", TimeSpan.FromMinutes(5));

        var entry = Assert.Single(await _db.AuditLog.ToListAsync());
        Assert.Equal(3, entry.OccurrenceCount);
        Assert.NotNull(entry.LastOccurredAtUtc);
    }

    [Fact]
    public async Task LogGroupedAsync_DifferentIpAddresses_AreNotGroupedTogether()
    {
        await _sut.LogGroupedAsync("Auth.TokenRejected", null, "Auth", "203.0.113.9", "TokenExpired", TimeSpan.FromMinutes(5));
        await _sut.LogGroupedAsync("Auth.TokenRejected", null, "Auth", "198.51.100.4", "TokenExpired", TimeSpan.FromMinutes(5));

        Assert.Equal(2, await _db.AuditLog.CountAsync());
    }

    [Fact]
    public async Task LogGroupedAsync_OutsideTheWindow_StartsANewGroup()
    {
        _db.AuditLog.Add(new AuditLogEntry
        {
            Action = "Auth.TokenRejected",
            IpAddress = "203.0.113.9",
            ObjectType = "Auth",
            OccurrenceCount = 1,
            Timestamp = DateTimeOffset.UtcNow.AddMinutes(-10),
        });
        await _db.SaveChangesAsync();

        await _sut.LogGroupedAsync("Auth.TokenRejected", null, "Auth", "203.0.113.9", "TokenExpired", TimeSpan.FromMinutes(5));

        Assert.Equal(2, await _db.AuditLog.CountAsync());
    }

    [Fact]
    public async Task CleanupExpiredAsync_RemovesOnlyEntriesOlderThanRetention()
    {
        _db.AuditLog.Add(new AuditLogEntry { Action = "File.Modify", Timestamp = DateTimeOffset.UtcNow.AddDays(-40) });
        _db.AuditLog.Add(new AuditLogEntry { Action = "File.Modify", Timestamp = DateTimeOffset.UtcNow.AddDays(-1) });
        await _db.SaveChangesAsync();

        var removed = await _sut.CleanupExpiredAsync(retentionDays: 30);

        Assert.Equal(1, removed);
        Assert.Single(await _db.AuditLog.ToListAsync());
    }

    [Fact]
    public async Task CleanupExpiredAsync_RunTwice_IsIdempotent()
    {
        _db.AuditLog.Add(new AuditLogEntry { Action = "File.Modify", Timestamp = DateTimeOffset.UtcNow.AddDays(-40) });
        await _db.SaveChangesAsync();

        await _sut.CleanupExpiredAsync(retentionDays: 30);
        var secondRun = await _sut.CleanupExpiredAsync(retentionDays: 30);

        Assert.Equal(0, secondRun);
    }
}
