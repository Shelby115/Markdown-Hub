using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Controllers.AI;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

public class AiPoolControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeAiService _ai = new();
    private readonly GenerationPoolService _pools;
    private readonly AiPoolController _sut;
    private readonly AiPoolAdminController _admin;

    public AiPoolControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var user = new AppUser { Username = "admin", NormalizedUsername = "ADMIN", IsAdministrator = true };
        _db.Users.Add(user);
        _db.SaveChanges();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim("preferred_username", user.Username),
                ]))
            }
        };
        var currentUser = new CurrentUserService(_db, httpContextAccessor);
        var audit = new AuditLogService(_db, httpContextAccessor);
        _pools = new GenerationPoolService(_db, _ai, new PoolActivityTracker());
        _sut = new AiPoolController(_pools, currentUser, audit);
        _admin = new AiPoolAdminController(_pools, new PoolActivityTracker(), currentUser, audit);
    }

    public void Dispose() => _db.Dispose();

    private async Task<GenerationPool> CreatePoolAsync() =>
        await _pools.CreatePoolAsync("Interactible", "- One brief interactible.\n", 5, true);

    private static T Value<T>(IActionResult result) => Assert.IsType<T>(Assert.IsType<OkObjectResult>(result).Value);

    [Fact]
    public async Task Forget_MarksTheEntryForgottenAndAudits()
    {
        var pool = await CreatePoolAsync();
        _ai.Respond = (_, _) => "A rusted lantern hangs here.";
        var entry = await _pools.GenerateEntryAsync(pool);

        var result = await _sut.Forget(entry!.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Equal(GenerationPoolEntryStatus.Forgotten, _db.GenerationPoolEntries.First().Status);
        Assert.Contains(_db.AuditLog, a => a.Action == "AiPool.ForgetEntry");
    }

    [Fact]
    public async Task Forget_UnknownEntry_IsNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(await _sut.Forget(999, CancellationToken.None));
    }

    [Fact]
    public async Task Create_ThenList_ReportsReadyCountAgainstTheTarget()
    {
        await _admin.Create(new SaveGenerationPoolRequest("Interactible", "- brief", 5, true), CancellationToken.None);

        var pools = Value<List<GenerationPoolDto>>(await _admin.List(CancellationToken.None));

        var pool = Assert.Single(pools);
        Assert.Equal("Interactible", pool.Name);
        Assert.Equal(0, pool.ReadyCount);
        Assert.Equal(5, pool.TargetCount);
    }

    [Fact]
    public async Task Create_InvalidName_IsRejected()
    {
        var result = await _admin.Create(new SaveGenerationPoolRequest("Bad: Name", "", 5, true), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Update_ChangesThePromptAndTarget()
    {
        var pool = await CreatePoolAsync();

        await _admin.Update(pool.Id, new SaveGenerationPoolRequest(pool.Name, "- Must mention rust.", 40, false), CancellationToken.None);

        var updated = await _pools.FindPoolAsync(pool.Id);
        Assert.Equal("- Must mention rust.", updated!.Instructions);
        Assert.Equal(40, updated.TargetCount);
        Assert.False(updated.Enabled);
    }

    [Fact]
    public async Task GenerateOne_AddsAnEntryImmediately()
    {
        var pool = await CreatePoolAsync();
        _ai.Respond = (_, _) => "A rusted lantern hangs here.";

        var entry = Value<GenerationPoolEntryDto>(await _admin.GenerateOne(pool.Id, CancellationToken.None));

        Assert.Equal("A rusted lantern hangs here.", entry.Content);
        Assert.Equal(1, await _pools.CountReadyAsync(pool.Id));
    }

    [Fact]
    public async Task GenerateOne_AiUnreachable_ReportsABadGateway()
    {
        var pool = await CreatePoolAsync();
        _ai.ThrowOnComplete = new AiServiceException("Ollama is unreachable.");

        var result = await _admin.GenerateOne(pool.Id, CancellationToken.None);

        Assert.Equal(StatusCodes.Status502BadGateway, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task SetSettings_SavesAndReportsWhetherTheGeneratorIsAllowedRightNow()
    {
        var result = await _admin.SetSettings(
            new GenerationPoolSettingsDto(Paused: true, "22:00", "06:00", 120, 30), CancellationToken.None);

        var status = Value<GenerationPoolStatusDto>(result);
        Assert.True(status.Settings.Paused);
        Assert.False(status.RunningNow);
        Assert.Equal("22:00", status.Settings.WindowStartUtc);
    }

    [Fact]
    public async Task SetSettings_InvalidWindowTime_IsRejected()
    {
        var result = await _admin.SetSettings(
            new GenerationPoolSettingsDto(false, "25:00", null, 60, 30), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Entries_ListsOnlyReadyOnes()
    {
        var pool = await CreatePoolAsync();
        _ai.Respond = (_, _) => "A rusted lantern hangs here.";
        await _pools.GenerateEntryAsync(pool);
        _ai.Respond = (_, _) => "A cracked bell sits in the corner.";
        var second = await _pools.GenerateEntryAsync(pool);
        await _pools.ForgetAsync(second!.Id);

        var entries = Value<List<GenerationPoolEntryDto>>(await _admin.Entries(pool.Id, CancellationToken.None));

        Assert.Equal("A rusted lantern hangs here.", Assert.Single(entries).Content);
    }
}
