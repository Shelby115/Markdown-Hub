using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Controllers;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

/// <summary>
/// Authorization boundaries and restore correctness for VersionsController. Regular users are
/// granted explicit FolderPermission rows (never IsAdministrator) so these tests exercise the
/// same permission model real non-admin users are subject to, per Activity-And-History.md
/// section 1.7 ("A user should not be able to inspect... history... unless the application's
/// existing permission model explicitly grants that access").
/// </summary>
public class VersionsControllerTests : IAsyncLifetime
{
    private readonly string _hubRoot = Directory.CreateTempSubdirectory("markdown-hub-tests-hub-").FullName;
    private readonly string _searchDbPath = Path.Combine(Path.GetTempPath(), $"markdown-hub-tests-search-{Guid.NewGuid():N}.db");
    private AppDbContext _db = null!;
    private HubPathService _hub = null!;
    private MarkdownFileService _files = null!;
    private AppUser _admin = null!;
    private AppUser _plainUser = null!;

    public async Task InitializeAsync()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hub:MarkdownRoot"] = _hubRoot,
                ["ConnectionStrings:Default"] = $"Data Source={_searchDbPath}",
            })
            .Build();
        _hub = new HubPathService(config);
        var search = new SearchIndexService(config);
        await search.EnsureSchemaAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var versions = new VersionService(_db);
        _files = new MarkdownFileService(_hub, _db, search, versions);

        _admin = new AppUser { KeycloakSubjectId = "admin-sub", Username = "admin", IsAdministrator = true };
        _plainUser = new AppUser { KeycloakSubjectId = "plain-sub", Username = "plain" };
        _db.Users.AddRange(_admin, _plainUser);
        await _db.SaveChangesAsync();
    }

    public Task DisposeAsync()
    {
        _db.Dispose();
        try { Directory.Delete(_hubRoot, recursive: true); } catch { /* best effort */ }
        try { File.Delete(_searchDbPath); } catch { /* best effort */ }
        return Task.CompletedTask;
    }

    private VersionsController BuildController(AppUser user)
    {
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user.KeycloakSubjectId),
                    new Claim("preferred_username", user.Username),
                ]))
            }
        };
        var currentUser = new CurrentUserService(_db, httpContextAccessor);
        var permissions = new PermissionService(_db, _hub);
        var versions = new VersionService(_db);
        var audit = new AuditLogService(_db, httpContextAccessor);
        return new VersionsController(_db, permissions, currentUser, versions, _files, audit);
    }

    private async Task GrantAsync(AppUser user, string folderPath, PermissionLevel level)
    {
        _db.FolderPermissions.Add(new FolderPermission { AppUserId = user.Id, FolderPath = folderPath, Level = level });
        await _db.SaveChangesAsync();
    }

    /// <summary>Produces two genuinely distinct, closed-then-open version rows - writing twice
    /// back-to-back would otherwise coalesce into a single open row (see VersionServiceTests),
    /// so the first write's open version is explicitly closed in between, simulating the
    /// coalescing window having elapsed.</summary>
    private async Task<int> SeedDocumentWithTwoVersionsAsync(string relativePath)
    {
        var first = await _files.WriteAsync(relativePath, "Version one", null, actingUserId: _admin.Id);
        var documentId = first.VersionResult.Version!.DocumentId;
        var open = await _db.DocumentVersions.SingleAsync(v => v.DocumentId == documentId && v.IsOpen);
        open.IsOpen = false;
        await _db.SaveChangesAsync();

        await _files.WriteAsync(relativePath, "Version two", null, actingUserId: _admin.Id);
        return documentId;
    }

    [Fact]
    public async Task GetHistoryByPath_UserWithoutAnyPermission_ReturnsForbid()
    {
        await SeedDocumentWithTwoVersionsAsync("Private/Secret.md");
        var sut = BuildController(_plainUser);

        var result = await sut.GetHistoryByPath("Private/Secret.md", CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task GetHistoryByPath_UserWithViewPermission_ReturnsHistory()
    {
        await SeedDocumentWithTwoVersionsAsync("Public/Notes.md");
        await GrantAsync(_plainUser, "Public", PermissionLevel.View);
        var sut = BuildController(_plainUser);

        var result = await sut.GetHistoryByPath("Public/Notes.md", CancellationToken.None);

        var dto = Assert.IsType<DocumentHistoryDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal(2, dto.Versions.Count);
    }

    [Fact]
    public async Task Restore_UserWithOnlyViewPermission_ReturnsForbid()
    {
        var documentId = await SeedDocumentWithTwoVersionsAsync("Public/Notes.md");
        await GrantAsync(_plainUser, "Public", PermissionLevel.View);
        var firstVersionId = (await new VersionService(_db).GetHistoryAsync(documentId)).Last().Id;
        var sut = BuildController(_plainUser);

        var result = await sut.Restore(firstVersionId, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Restore_UserWithEditPermission_Succeeds_AndDoesNotRemoveAnyExistingVersion()
    {
        var documentId = await SeedDocumentWithTwoVersionsAsync("Public/Notes.md");
        await GrantAsync(_plainUser, "Public", PermissionLevel.Edit);
        var versionsService = new VersionService(_db);
        var firstVersion = (await versionsService.GetHistoryAsync(documentId)).Last(); // oldest ("Version one")
        var sut = BuildController(_plainUser);

        var result = await sut.Restore(firstVersion.Id, CancellationToken.None);

        var dto = Assert.IsType<VersionDetailDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("Version one", dto.Content);
        Assert.Equal(DocumentVersionType.Restore, dto.VersionType);

        var allVersions = await versionsService.GetHistoryAsync(documentId);
        Assert.Equal(3, allVersions.Count); // original two + the new restore version - nothing removed
    }

    [Fact]
    public async Task Restore_RestoredContentExactlyMatchesTheSelectedHistoricalVersion()
    {
        var documentId = await SeedDocumentWithTwoVersionsAsync("Public/Notes.md");
        await GrantAsync(_plainUser, "Public", PermissionLevel.Edit);
        var firstVersion = (await new VersionService(_db).GetHistoryAsync(documentId)).Last();
        var sut = BuildController(_plainUser);

        await sut.Restore(firstVersion.Id, CancellationToken.None);

        var onDisk = await File.ReadAllTextAsync(Path.Combine(_hubRoot, "Public", "Notes.md"));
        Assert.Equal("Version one", onDisk);
    }

    [Fact]
    public async Task Restore_OnADeletedDocument_EditPermissionIsNotEnough_RequiresManage()
    {
        var documentId = await SeedDocumentWithTwoVersionsAsync("Public/Notes.md");
        await _files.DeleteAsync("Public/Notes.md", actingUserId: _admin.Id);
        await GrantAsync(_plainUser, "Public", PermissionLevel.Edit);
        var versionId = (await new VersionService(_db).GetHistoryAsync(documentId)).First().Id;
        var sut = BuildController(_plainUser);

        var result = await sut.Restore(versionId, CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Restore_OnADeletedDocument_WithManagePermission_UndeletesIt()
    {
        var documentId = await SeedDocumentWithTwoVersionsAsync("Public/Notes.md");
        await _files.DeleteAsync("Public/Notes.md", actingUserId: _admin.Id);
        await GrantAsync(_plainUser, "Public", PermissionLevel.Manage);
        var versionId = (await new VersionService(_db).GetHistoryAsync(documentId)).First().Id;
        var sut = BuildController(_plainUser);

        var result = await sut.Restore(versionId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
        var meta = await _db.Pages.SingleAsync(p => p.Id == documentId);
        Assert.False(meta.IsDeleted);
        Assert.True(File.Exists(Path.Combine(_hubRoot, "Public", "Notes.md")));
    }

    [Fact]
    public async Task Compare_ReturnsBothVersionsFullContent()
    {
        var documentId = await SeedDocumentWithTwoVersionsAsync("Public/Notes.md");
        var all = await new VersionService(_db).GetHistoryAsync(documentId);
        var sut = BuildController(_admin);

        var result = await sut.Compare(all.Last().Id, all.First().Id, CancellationToken.None);

        var dto = Assert.IsType<CompareResultDto>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("Version one", dto.From.Content);
        Assert.Equal("Version two", dto.To.Content);
    }

    [Fact]
    public async Task Compare_VersionsFromDifferentDocuments_ReturnsBadRequest()
    {
        var doc1 = await SeedDocumentWithTwoVersionsAsync("Public/A.md");
        var doc2 = await SeedDocumentWithTwoVersionsAsync("Public/B.md");
        var v1 = (await new VersionService(_db).GetHistoryAsync(doc1)).First();
        var v2 = (await new VersionService(_db).GetHistoryAsync(doc2)).First();
        var sut = BuildController(_admin);

        var result = await sut.Compare(v1.Id, v2.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ListDeleted_OnlyReturnsDocumentsTheUserHasManageAccessTo()
    {
        await SeedDocumentWithTwoVersionsAsync("Public/Notes.md");
        await _files.DeleteAsync("Public/Notes.md", actingUserId: _admin.Id);
        await SeedDocumentWithTwoVersionsAsync("Private/Secret.md");
        await _files.DeleteAsync("Private/Secret.md", actingUserId: _admin.Id);
        await GrantAsync(_plainUser, "Public", PermissionLevel.Manage);
        var sut = BuildController(_plainUser);

        var result = await sut.ListDeleted(CancellationToken.None);

        var items = Assert.IsAssignableFrom<IEnumerable<DeletedDocumentDto>>(Assert.IsType<OkObjectResult>(result).Value).ToList();
        var item = Assert.Single(items);
        Assert.Equal("Public/Notes.md", item.RelativePath);
        Assert.NotNull(item.LatestVersionId);
    }

    [Fact]
    public async Task ListDeleted_Admin_SeesEveryDeletedDocument()
    {
        await SeedDocumentWithTwoVersionsAsync("Public/Notes.md");
        await _files.DeleteAsync("Public/Notes.md", actingUserId: _admin.Id);
        await SeedDocumentWithTwoVersionsAsync("Private/Secret.md");
        await _files.DeleteAsync("Private/Secret.md", actingUserId: _admin.Id);
        var sut = BuildController(_admin);

        var result = await sut.ListDeleted(CancellationToken.None);

        var items = Assert.IsAssignableFrom<IEnumerable<DeletedDocumentDto>>(Assert.IsType<OkObjectResult>(result).Value).ToList();
        Assert.Equal(2, items.Count);
    }
}
