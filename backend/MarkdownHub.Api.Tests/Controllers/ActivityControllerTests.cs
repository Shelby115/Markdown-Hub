using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Controllers.Admin;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities.Admin;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

public class ActivityControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly ActivityController _sut;
    private readonly AppUser _alice;
    private readonly AppUser _bob;

    public ActivityControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _alice = new AppUser { Username = "alice", NormalizedUsername = "ALICE" };
        _bob = new AppUser { Username = "bob", NormalizedUsername = "BOB" };
        _db.Users.AddRange(_alice, _bob);
        _db.SaveChanges();

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build();
        var settings = new HistorySettingsService(_db, config);
        _sut = new ActivityController(_db, settings);
    }

    public void Dispose() => _db.Dispose();

    private void Seed(AppUser? user, string action, DateTimeOffset timestamp, string? targetPath = null, string? objectType = null)
    {
        _db.AuditLog.Add(new AuditLogEntry
        {
            AppUserId = user?.Id,
            Action = action,
            TargetPath = targetPath,
            ObjectType = objectType,
            Timestamp = timestamp,
        });
        _db.SaveChanges();
    }

    [Fact]
    public async Task Query_NoFilters_ReturnsNewestFirst()
    {
        Seed(_alice, "File.Modify", DateTimeOffset.UtcNow.AddHours(-2));
        Seed(_bob, "Auth.Login", DateTimeOffset.UtcNow.AddHours(-1));

        var result = await _sut.Query(null, null, null, null, null, ct: CancellationToken.None);

        var page = Assert.IsType<ActivityPageDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, page.Items.Count);
        Assert.Equal("Auth.Login", page.Items[0].Action); // most recent first
    }

    [Fact]
    public async Task Query_FiltersByUser()
    {
        Seed(_alice, "File.Modify", DateTimeOffset.UtcNow);
        Seed(_bob, "Auth.Login", DateTimeOffset.UtcNow);

        var result = await _sut.Query(null, null, _bob.Id, null, null, ct: CancellationToken.None);

        var page = Assert.IsType<ActivityPageDto>(Assert.IsType<OkObjectResult>(result).Value);
        var item = Assert.Single(page.Items);
        Assert.Equal("bob", item.Username);
    }

    [Fact]
    public async Task Query_FiltersByActionType()
    {
        Seed(_alice, "File.Modify", DateTimeOffset.UtcNow);
        Seed(_alice, "Auth.Login", DateTimeOffset.UtcNow);

        var result = await _sut.Query(null, null, null, "Auth.Login", null, ct: CancellationToken.None);

        var page = Assert.IsType<ActivityPageDto>(Assert.IsType<OkObjectResult>(result).Value);
        var item = Assert.Single(page.Items);
        Assert.Equal("Auth.Login", item.Action);
    }

    [Fact]
    public async Task Query_FiltersByObjectSearch_SubstringMatchOnTargetPath()
    {
        Seed(_alice, "File.Modify", DateTimeOffset.UtcNow, targetPath: "Campaign/Session 5.md");
        Seed(_alice, "File.Modify", DateTimeOffset.UtcNow, targetPath: "Recipes/Pie.md");

        var result = await _sut.Query(null, null, null, null, "Session", ct: CancellationToken.None);

        var page = Assert.IsType<ActivityPageDto>(Assert.IsType<OkObjectResult>(result).Value);
        var item = Assert.Single(page.Items);
        Assert.Equal("Campaign/Session 5.md", item.TargetPath);
    }

    [Fact]
    public async Task Query_DefaultsToTheConfiguredDefaultDaysWindow()
    {
        await new HistorySettingsService(_db, new ConfigurationBuilder().Build())
            .SetAsync(HistorySettingsService.ActivityDefaultDaysKey, 7);
        Seed(_alice, "File.Modify", DateTimeOffset.UtcNow.AddDays(-3)); // within 7 days
        Seed(_alice, "File.Modify", DateTimeOffset.UtcNow.AddDays(-10)); // outside 7 days

        var result = await _sut.Query(null, null, null, null, null, ct: CancellationToken.None);

        var page = Assert.IsType<ActivityPageDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Query_ExplicitFromBeyondRetention_IsClampedToTheRetentionWindow()
    {
        await new HistorySettingsService(_db, new ConfigurationBuilder().Build())
            .SetAsync(HistorySettingsService.ActivityRetentionDaysKey, 30);
        Seed(_alice, "File.Modify", DateTimeOffset.UtcNow.AddDays(-25)); // within 30-day retention

        // Ask for the last 90 days - should be clamped to the 30-day retention ceiling, not fail.
        var result = await _sut.Query(DateTimeOffset.UtcNow.AddDays(-90), null, null, null, null, ct: CancellationToken.None);

        var page = Assert.IsType<ActivityPageDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Single(page.Items);
    }

    [Fact]
    public async Task Query_Pagination_SplitsResultsAcrossPages()
    {
        for (var i = 0; i < 5; i++)
        {
            Seed(_alice, "File.Modify", DateTimeOffset.UtcNow.AddMinutes(-i));
        }

        var page1 = Assert.IsType<ActivityPageDto>(Assert.IsType<OkObjectResult>(
            await _sut.Query(null, null, null, null, null, page: 1, pageSize: 2, ct: CancellationToken.None)).Value);
        var page2 = Assert.IsType<ActivityPageDto>(Assert.IsType<OkObjectResult>(
            await _sut.Query(null, null, null, null, null, page: 2, pageSize: 2, ct: CancellationToken.None)).Value);

        Assert.Equal(2, page1.Items.Count);
        Assert.Equal(2, page2.Items.Count);
        Assert.Equal(5, page1.TotalCount);
        Assert.NotEqual(page1.Items[0].Id, page2.Items[0].Id);
    }

    [Fact]
    public async Task GetDetail_IncludesIpAddressAndDetails()
    {
        _db.AuditLog.Add(new AuditLogEntry
        {
            AppUserId = _alice.Id,
            Action = "File.Modify",
            TargetPath = "Notes.md",
            IpAddress = "203.0.113.7",
            Details = "extra info",
            Timestamp = DateTimeOffset.UtcNow,
        });
        await _db.SaveChangesAsync();
        var id = (await _db.AuditLog.SingleAsync()).Id;

        var result = await _sut.GetDetail(id, CancellationToken.None);

        var dto = Assert.IsType<ActivityDetailDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("203.0.113.7", dto.IpAddress);
        Assert.Equal("extra info", dto.Details);
    }

    [Fact]
    public async Task GetDetail_UnknownId_ReturnsNotFound()
    {
        var result = await _sut.GetDetail(999, CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    /// <summary>
    /// The global activity API is admin-only (section 2.7) purely via the controller's
    /// [Authorize(Policy = "RequireAdministrator")] attribute - there's no additional in-method
    /// check, since the ASP.NET authorization pipeline (not exercised by directly invoking a
    /// controller method, as every other test here does) is what actually enforces it. This
    /// guards against that attribute ever being accidentally removed.
    /// </summary>
    [Fact]
    public void Controller_IsGatedByTheRequireAdministratorPolicy()
    {
        var attribute = Assert.Single(
            typeof(ActivityController).GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true))
            as Microsoft.AspNetCore.Authorization.AuthorizeAttribute;

        Assert.NotNull(attribute);
        Assert.Equal("RequireAdministrator", attribute!.Policy);
    }
}
