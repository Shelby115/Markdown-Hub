using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Controllers.Admin;
using MarkdownHub.Api.Controllers.Auth;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Data.Entities.Admin;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

/// <summary>
/// EF Core's SQLite provider cannot translate a direct comparison operator (&lt;, &gt;=, etc.)
/// against a DateTimeOffset column into SQL - it throws at query time rather than falling back
/// silently. This reached production once already: HistoryCleanupHostedService logged "History
/// cleanup failed" on startup because VersionService.CleanupExpiredVersionsAsync did exactly
/// that. EF Core's InMemory provider (every other test in this project) never SQL-translates
/// anything, so it couldn't catch this - the same class of gap DatabaseMigrationsTests exists
/// for. These tests run the affected methods against a real SQLite file to guard against it
/// recurring in either these methods or any future date-range query.
/// </summary>
public class RealSqliteDateTimeOffsetTests : IAsyncLifetime
{
    private readonly string _dbPath = Path.Combine(Path.GetTempPath(), $"markdown-hub-tests-datetime-{Guid.NewGuid():N}.db");
    private AppDbContext _db = null!;

    public Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={_dbPath}")
            .Options;
        _db = new AppDbContext(options);
        return _db.Database.EnsureCreatedAsync();
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        try { File.Delete(_dbPath); } catch { /* best effort */ }
        return Task.CompletedTask;
    }

    [Fact]
    public async Task VersionService_CleanupExpiredVersionsAsync_WorksAgainstRealSqlite()
    {
        _db.DocumentVersions.Add(new DocumentVersion { DocumentId = 1, Content = "old", RelativePath = "A.md", CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-10), IsOpen = false });
        _db.DocumentVersions.Add(new DocumentVersion { DocumentId = 1, Content = "recent", RelativePath = "A.md", CreatedAtUtc = DateTimeOffset.UtcNow.AddDays(-1), IsOpen = false });
        await _db.SaveChangesAsync();
        var sut = new VersionService(_db);

        var removed = await sut.CleanupExpiredVersionsAsync(retentionDays: 3);

        Assert.Equal(1, removed);
    }

    [Fact]
    public async Task AuditLogService_CleanupExpiredAsync_WorksAgainstRealSqlite()
    {
        _db.AuditLog.Add(new AuditLogEntry { Action = "File.Modify", Timestamp = DateTimeOffset.UtcNow.AddDays(-40) });
        _db.AuditLog.Add(new AuditLogEntry { Action = "File.Modify", Timestamp = DateTimeOffset.UtcNow.AddDays(-1) });
        await _db.SaveChangesAsync();
        var sut = new AuditLogService(_db, new Microsoft.AspNetCore.Http.HttpContextAccessor());

        var removed = await sut.CleanupExpiredAsync(retentionDays: 30);

        Assert.Equal(1, removed);
    }

    [Fact]
    public async Task GenerationPoolService_CleanupUsedEntriesAsync_WorksAgainstRealSqlite()
    {
        _db.GenerationPoolEntries.Add(new GenerationPoolEntry
        {
            PoolId = 1, Content = "old", ContentHash = "A",
            Status = GenerationPoolEntryStatus.Used, SpentAtUtc = DateTimeOffset.UtcNow.AddDays(-100),
        });
        _db.GenerationPoolEntries.Add(new GenerationPoolEntry
        {
            PoolId = 1, Content = "recent", ContentHash = "B",
            Status = GenerationPoolEntryStatus.Used, SpentAtUtc = DateTimeOffset.UtcNow.AddDays(-1),
        });
        await _db.SaveChangesAsync();
        var sut = new GenerationPoolService(_db, new Controllers.FakeAiService());

        Assert.Equal(1, await sut.CleanupUsedEntriesAsync(retentionDays: 90));
    }

    [Fact]
    public async Task AuditLogService_LogGroupedAsync_WorksAgainstRealSqlite()
    {
        var sut = new AuditLogService(_db, new Microsoft.AspNetCore.Http.HttpContextAccessor());

        await sut.LogGroupedAsync("Auth.TokenRejected", null, "Auth", "203.0.113.9", "TokenExpired", TimeSpan.FromMinutes(5));
        await sut.LogGroupedAsync("Auth.TokenRejected", null, "Auth", "203.0.113.9", "TokenExpired", TimeSpan.FromMinutes(5));

        var entry = Assert.Single(await _db.AuditLog.ToListAsync());
        Assert.Equal(2, entry.OccurrenceCount);
    }

    [Fact]
    public async Task ActivityController_Query_DateRangeFilteringWorksAgainstRealSqlite()
    {
        _db.AuditLog.Add(new AuditLogEntry { Action = "File.Modify", Timestamp = DateTimeOffset.UtcNow.AddDays(-20) });
        _db.AuditLog.Add(new AuditLogEntry { Action = "File.Modify", Timestamp = DateTimeOffset.UtcNow.AddDays(-1) });
        await _db.SaveChangesAsync();
        var config = new ConfigurationBuilder().Build();
        var settings = new HistorySettingsService(_db, config);
        var sut = new ActivityController(_db, settings);

        var result = await sut.Query(
            DateTimeOffset.UtcNow.AddDays(-3), DateTimeOffset.UtcNow, null, null, null, ct: CancellationToken.None);

        var page = Assert.IsType<ActivityPageDto>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result).Value);
        Assert.Single(page.Items); // only the 1-day-old entry falls inside the 3-day window
    }

    /// <summary>Caught during manual Docker verification of the auth redesign (not by the
    /// InMemory-backed AccountControllerTests): GetSessions originally ordered by LastActivityAt
    /// (DateTimeOffset) directly in the query, which throws against real SQLite the same way the
    /// methods above do.</summary>
    [Fact]
    public async Task AccountController_GetSessions_WorksAgainstRealSqlite()
    {
        var user = new AppUser { Username = "alice", NormalizedUsername = "ALICE" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _db.Sessions.Add(new Session { UserId = user.Id, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), LastActivityAt = DateTimeOffset.UtcNow.AddMinutes(-10) });
        _db.Sessions.Add(new Session { UserId = user.Id, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), LastActivityAt = DateTimeOffset.UtcNow });
        await _db.SaveChangesAsync();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, user.Id.ToString())]))
            }
        };
        var sut = new AccountController(
            new CurrentUserService(_db, httpContextAccessor), _db, new AuditLogService(_db, httpContextAccessor),
            new PasswordHasher<AppUser>(), new AccountSafetyService(_db))
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContextAccessor.HttpContext },
        };

        var result = await sut.GetSessions(CancellationToken.None);

        var sessions = Assert.IsAssignableFrom<IEnumerable<SessionResponse>>(
            Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result).Value).ToList();
        Assert.Equal(2, sessions.Count);
        Assert.True(sessions[0].LastActivityAt >= sessions[1].LastActivityAt); // most recent first
    }
}
