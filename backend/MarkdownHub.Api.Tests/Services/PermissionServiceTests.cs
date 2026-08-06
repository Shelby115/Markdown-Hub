using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class PermissionServiceTests : IDisposable
{
    private readonly string _hubRoot;
    private readonly AppDbContext _db;
    private readonly HubPathService _hub;
    private readonly PermissionService _sut;

    public PermissionServiceTests()
    {
        _hubRoot = Directory.CreateTempSubdirectory("markdown-hub-tests-").FullName;

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Hub:MarkdownRoot"] = _hubRoot })
            .Build();
        _hub = new HubPathService(config);

        _sut = new PermissionService(_db, _hub);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_hubRoot, recursive: true); } catch { /* best effort cleanup */ }
    }

    private async Task<AppUser> CreateUserWithGrantAsync(string folderPath, PermissionLevel level)
    {
        var user = new AppUser { Username = "alice", NormalizedUsername = "ALICE" };
        _db.Users.Add(user);
        _db.FolderPermissions.Add(new FolderPermission { AppUserId = 0, FolderPath = folderPath, Level = level });
        await _db.SaveChangesAsync();
        // FolderPermission.AppUserId needs the real generated id - InMemory provider assigns Id on SaveChanges.
        var grant = await _db.FolderPermissions.FirstAsync();
        grant.AppUserId = user.Id;
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task GetEffectiveLevelAsync_WithinGrantedFolder_ReturnsGrantedLevel()
    {
        var user = await CreateUserWithGrantAsync("Public", PermissionLevel.Edit);

        var level = await _sut.GetEffectiveLevelAsync(user.Id, "Public/notes.md");

        Assert.Equal(PermissionLevel.Edit, level);
    }

    [Fact]
    public async Task GetEffectiveLevelAsync_OutsideGrantedFolder_ReturnsNull()
    {
        var user = await CreateUserWithGrantAsync("Public", PermissionLevel.Edit);

        var level = await _sut.GetEffectiveLevelAsync(user.Id, "Private/secret.md");

        Assert.Null(level);
    }

    /// <summary>
    /// This is the path-traversal permission bypass: a user granted access to "Public" must
    /// NOT gain access to "Private" just because the raw relativePath string happens to start
    /// with "Public/" before a "../" segment is resolved. HubPathService.ResolveSafe (used by
    /// the actual file read/write layer) collapses "../", so the permission check has to agree
    /// with that same canonical form or the two layers disagree about which folder a path is in.
    /// </summary>
    [Fact]
    public async Task GetEffectiveLevelAsync_TraversalOutOfGrantedFolder_ReturnsNull()
    {
        var user = await CreateUserWithGrantAsync("Public", PermissionLevel.Edit);

        var level = await _sut.GetEffectiveLevelAsync(user.Id, "Public/../Private/secret.md");

        Assert.Null(level);
    }

    [Fact]
    public async Task HasAtLeastAsync_TraversalOutOfGrantedFolder_ReturnsFalse()
    {
        var user = await CreateUserWithGrantAsync("Public", PermissionLevel.Edit);

        var allowed = await _sut.HasAtLeastAsync(user.Id, "Public/../Private/secret.md", PermissionLevel.View);

        Assert.False(allowed);
    }

    [Fact]
    public async Task GetEffectiveLevelAsync_PathEscapingHubRoot_ReturnsNull()
    {
        var user = await CreateUserWithGrantAsync("", PermissionLevel.Edit); // root grant

        var level = await _sut.GetEffectiveLevelAsync(user.Id, "../../outside/secret.md");

        Assert.Null(level);
    }

    [Fact]
    public async Task GetEffectiveLevelAsync_RootGrant_AppliesEverywhere()
    {
        var user = await CreateUserWithGrantAsync("", PermissionLevel.View);

        var level = await _sut.GetEffectiveLevelAsync(user.Id, "Anywhere/notes.md");

        Assert.Equal(PermissionLevel.View, level);
    }

    [Fact]
    public async Task GetEffectiveLevelAsync_MostSpecificGrantWins()
    {
        var user = new AppUser { Username = "bob", NormalizedUsername = "BOB" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        _db.FolderPermissions.Add(new FolderPermission { AppUserId = user.Id, FolderPath = "Projects", Level = PermissionLevel.View });
        _db.FolderPermissions.Add(new FolderPermission { AppUserId = user.Id, FolderPath = "Projects/Secret", Level = PermissionLevel.Manage });
        await _db.SaveChangesAsync();

        var level = await _sut.GetEffectiveLevelAsync(user.Id, "Projects/Secret/plan.md");

        Assert.Equal(PermissionLevel.Manage, level);
    }

    [Fact]
    public async Task GetEffectiveLevelAsync_AdministratorBypassesGrants()
    {
        var user = new AppUser { Username = "admin", NormalizedUsername = "ADMIN", IsAdministrator = true };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var level = await _sut.GetEffectiveLevelAsync(user.Id, "Anything/anywhere.md");

        Assert.Equal(PermissionLevel.Manage, level);
    }

    [Fact]
    public async Task GetGrantsAsync_ThenSyncHasAtLeast_MatchesAsyncEquivalent()
    {
        var user = await CreateUserWithGrantAsync("Public", PermissionLevel.Edit);
        var grants = await _sut.GetGrantsAsync(user.Id);

        // Batched (sync, pre-fetched grants) path used by tree/list endpoints must agree with
        // the single-path async path used everywhere else - same traversal protections included.
        Assert.True(_sut.HasAtLeast(user, grants, "Public/notes.md", PermissionLevel.Edit));
        Assert.False(_sut.HasAtLeast(user, grants, "Public/../Private/secret.md", PermissionLevel.View));
        Assert.False(_sut.HasAtLeast(user, grants, "Private/secret.md", PermissionLevel.View));
    }

    [Fact]
    public async Task GetEffectiveLevelAsync_DisabledUser_ReturnsNull()
    {
        var user = new AppUser { Username = "gone", NormalizedUsername = "GONE", IsDisabled = true };
        _db.Users.Add(user);
        _db.FolderPermissions.Add(new FolderPermission { AppUserId = 0, FolderPath = "", Level = PermissionLevel.Manage });
        await _db.SaveChangesAsync();
        var grant = await _db.FolderPermissions.FirstAsync();
        grant.AppUserId = user.Id;
        await _db.SaveChangesAsync();

        var level = await _sut.GetEffectiveLevelAsync(user.Id, "notes.md");

        Assert.Null(level);
    }
}
