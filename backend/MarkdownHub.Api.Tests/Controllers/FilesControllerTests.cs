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

public class FilesControllerTests : IDisposable
{
    private readonly string _hubRoot;
    private readonly string _searchDbPath;
    private readonly IConfiguration _config;
    private readonly AppDbContext _db;
    private readonly HubPathService _hub;
    private readonly FilesController _sut;

    public FilesControllerTests()
    {
        _hubRoot = Directory.CreateTempSubdirectory("markdown-hub-tests-hub-").FullName;
        // A real temp file, not ":memory:" - SearchIndexService opens its own ADO.NET connection
        // per call, and a plain ":memory:" connection string gives each of those a fresh,
        // schema-less database rather than a shared one.
        _searchDbPath = Path.Combine(Path.GetTempPath(), $"markdown-hub-tests-search-{Guid.NewGuid():N}.db");

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Hub:MarkdownRoot"] = _hubRoot,
                ["ConnectionStrings:Default"] = $"Data Source={_searchDbPath}",
            })
            .Build();
        _hub = new HubPathService(_config);
        new SearchIndexService(_config).EnsureSchemaAsync().GetAwaiter().GetResult();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var admin = new AppUser { Username = "admin", NormalizedUsername = "ADMIN", IsAdministrator = true };
        _db.Users.Add(admin);
        _db.SaveChanges();

        _sut = BuildController(admin);
    }

    private FilesController BuildController(AppUser user)
    {
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
        var permissions = new PermissionService(_db, _hub);
        var search = new SearchIndexService(_config);
        var versions = new VersionService(_db);
        var files = new MarkdownFileService(_hub, _db, search, versions);
        var renderer = new MarkdownRenderService();
        var audit = new AuditLogService(_db, httpContextAccessor);
        return new FilesController(files, _hub, permissions, currentUser, renderer, _db, audit);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_hubRoot, recursive: true); } catch { /* best effort */ }
        try { File.Delete(_searchDbPath); } catch { /* best effort */ }
    }

    [Fact]
    public async Task CreateFolder_CreatesTheDirectoryOnDisk()
    {
        var result = await _sut.CreateFolder("NewFolder", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.True(Directory.Exists(Path.Combine(_hubRoot, "NewFolder")));
    }

    [Fact]
    public async Task CreateFolder_NestedPath_CreatesIntermediateDirectories()
    {
        var result = await _sut.CreateFolder("Parent/Child", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.True(Directory.Exists(Path.Combine(_hubRoot, "Parent", "Child")));
    }

    [Fact]
    public async Task CreateFolder_AlreadyExists_ReturnsConflict()
    {
        await _sut.CreateFolder("Existing", CancellationToken.None);

        var result = await _sut.CreateFolder("Existing", CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task CreateFolder_PathCollidesWithExistingFile_ReturnsConflict()
    {
        await File.WriteAllTextAsync(Path.Combine(_hubRoot, "notes.md"), "# hi");

        var result = await _sut.CreateFolder("notes.md", CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
    }

    [Fact]
    public async Task CreateFolder_UserWithoutEditPermission_ReturnsForbid()
    {
        var plainUser = new AppUser { Username = "plain", NormalizedUsername = "PLAIN", IsAdministrator = false };
        _db.Users.Add(plainUser);
        _db.SaveChanges();
        var controller = BuildController(plainUser);

        var result = await controller.CreateFolder("NewFolder", CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.False(Directory.Exists(Path.Combine(_hubRoot, "NewFolder")));
    }

    [Fact]
    public async Task SavePage_NewPage_LogsFileCreate_LinkedToTheNewVersion()
    {
        await _sut.SavePage("Notes.md", new SavePageRequest("Hello world.", null), CancellationToken.None);

        var entry = await _db.AuditLog.SingleAsync();
        Assert.Equal("File.Create", entry.Action);
        Assert.Equal("Document", entry.ObjectType);
        Assert.NotNull(entry.RelatedVersionId);

        var version = await _db.DocumentVersions.SingleAsync(v => v.Id == entry.RelatedVersionId);
        Assert.Equal("Hello world.", version.Content);
        Assert.Equal(entry.ObjectId, version.DocumentId);
    }

    [Fact]
    public async Task SavePage_MeaningfulEdit_LogsFileModify_NotFileCreate()
    {
        await _sut.SavePage("Notes.md", new SavePageRequest("Hello world.", null), CancellationToken.None);
        // Simulate the coalescing window having elapsed so the edit below produces a distinct
        // version/event rather than silently updating the just-created one in place.
        var open = await _db.DocumentVersions.SingleAsync(v => v.IsOpen);
        open.IsOpen = false;
        await _db.SaveChangesAsync();

        await _sut.SavePage("Notes.md", new SavePageRequest("Hello world.\nMore.", null), CancellationToken.None);

        var entries = await _db.AuditLog.OrderBy(a => a.Id).ToListAsync();
        Assert.Equal(2, entries.Count);
        Assert.Equal("File.Create", entries[0].Action);
        Assert.Equal("File.Modify", entries[1].Action);
    }

    [Fact]
    public async Task SavePage_ResavingIdenticalContent_DoesNotLogAnActivityEvent()
    {
        await _sut.SavePage("Notes.md", new SavePageRequest("Hello world.", null), CancellationToken.None);

        await _sut.SavePage("Notes.md", new SavePageRequest("Hello world.", null), CancellationToken.None);

        Assert.Single(await _db.AuditLog.ToListAsync()); // only the original create, no event for the no-op resave
    }

    [Fact]
    public async Task DeletePage_LogsFileDelete_AndPreservesTheDocumentRowAsSoftDeleted()
    {
        await _sut.SavePage("Notes.md", new SavePageRequest("Hello world.", null), CancellationToken.None);

        await _sut.DeletePage("Notes.md", CancellationToken.None);

        var deleteEntry = await _db.AuditLog.SingleAsync(a => a.Action == "File.Delete");
        Assert.Equal("Document", deleteEntry.ObjectType);
        var meta = await _db.Pages.SingleAsync(p => p.Id == deleteEntry.ObjectId);
        Assert.True(meta.IsDeleted);
    }

    [Fact]
    public async Task Rename_SameFolder_LogsFileRename()
    {
        await _sut.SavePage("Old.md", new SavePageRequest("Content", null), CancellationToken.None);

        await _sut.Rename("Old.md", new RenameRequest("New.md"), CancellationToken.None);

        var entry = await _db.AuditLog.SingleAsync(a => a.Action == "File.Rename" || a.Action == "File.Move");
        Assert.Equal("File.Rename", entry.Action);
    }

    [Fact]
    public async Task Rename_DifferentFolder_LogsFileMove()
    {
        await _sut.SavePage("Old.md", new SavePageRequest("Content", null), CancellationToken.None);

        await _sut.Rename("Old.md", new RenameRequest("Folder/Old.md"), CancellationToken.None);

        var entry = await _db.AuditLog.SingleAsync(a => a.Action == "File.Rename" || a.Action == "File.Move");
        Assert.Equal("File.Move", entry.Action);
    }

    [Fact]
    public async Task RenameFolder_MovesTheDirectoryOnDisk()
    {
        await _sut.CreateFolder("Campaign", CancellationToken.None);

        var result = await _sut.RenameFolder("Campaign", new RenameRequest("Old Campaign"), CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(Directory.Exists(Path.Combine(_hubRoot, "Campaign")));
        Assert.True(Directory.Exists(Path.Combine(_hubRoot, "Old Campaign")));
    }

    [Fact]
    public async Task RenameFolder_UpdatesEveryContainedDocumentsPath_PreservingItsId()
    {
        await _sut.SavePage("Campaign/Session 1.md", new SavePageRequest("Notes", null), CancellationToken.None);
        var before = await _db.Pages.SingleAsync(p => p.RelativePath == "Campaign/Session 1.md");

        await _sut.RenameFolder("Campaign", new RenameRequest("Old Campaign"), CancellationToken.None);

        var after = await _db.Pages.SingleAsync(p => p.Id == before.Id);
        Assert.Equal("Old Campaign/Session 1.md", after.RelativePath);
        Assert.Equal("Session 1", after.PageName); // unaffected - only the folder prefix moved
    }

    [Fact]
    public async Task RenameFolder_UpdatesNestedSubfolderContentsToo()
    {
        await _sut.SavePage("Campaign/Sessions/Session 1.md", new SavePageRequest("Notes", null), CancellationToken.None);

        await _sut.RenameFolder("Campaign", new RenameRequest("Old Campaign"), CancellationToken.None);

        Assert.True(await _db.Pages.AnyAsync(p => p.RelativePath == "Old Campaign/Sessions/Session 1.md"));
    }

    [Fact]
    public async Task RenameFolder_PreservesVersionHistoryOfContainedDocuments()
    {
        await _sut.SavePage("Campaign/Session 1.md", new SavePageRequest("Notes", null), CancellationToken.None);
        var documentId = (await _db.Pages.SingleAsync(p => p.RelativePath == "Campaign/Session 1.md")).Id;

        await _sut.RenameFolder("Campaign", new RenameRequest("Old Campaign"), CancellationToken.None);

        Assert.NotEmpty(await _db.DocumentVersions.Where(v => v.DocumentId == documentId).ToListAsync());
    }

    [Fact]
    public async Task RenameFolder_UpdatesFolderPermissionsPointingAtItOrNestedInsideIt()
    {
        var otherUser = new AppUser { Username = "other", NormalizedUsername = "OTHER" };
        _db.Users.Add(otherUser);
        await _db.SaveChangesAsync();
        _db.FolderPermissions.Add(new FolderPermission { AppUserId = otherUser.Id, FolderPath = "Campaign", Level = PermissionLevel.Edit });
        _db.FolderPermissions.Add(new FolderPermission { AppUserId = otherUser.Id, FolderPath = "Campaign/Private", Level = PermissionLevel.Manage });
        await _db.SaveChangesAsync();
        await _sut.CreateFolder("Campaign", CancellationToken.None);

        await _sut.RenameFolder("Campaign", new RenameRequest("Old Campaign"), CancellationToken.None);

        var permissions = await _db.FolderPermissions.Where(p => p.AppUserId == otherUser.Id).ToListAsync();
        Assert.Contains(permissions, p => p.FolderPath == "Old Campaign");
        Assert.Contains(permissions, p => p.FolderPath == "Old Campaign/Private");
    }

    [Fact]
    public async Task RenameFolder_LogsFolderRename_WhenStayingInTheSameParent()
    {
        await _sut.CreateFolder("Campaign", CancellationToken.None);

        await _sut.RenameFolder("Campaign", new RenameRequest("Old Campaign"), CancellationToken.None);

        var entry = await _db.AuditLog.SingleAsync(a => a.Action == "Folder.Rename" || a.Action == "Folder.Move");
        Assert.Equal("Folder.Rename", entry.Action);
    }

    [Fact]
    public async Task RenameFolder_LogsFolderMove_WhenTheParentChanges()
    {
        await _sut.CreateFolder("Campaign", CancellationToken.None);
        await _sut.CreateFolder("Archive", CancellationToken.None);

        await _sut.RenameFolder("Campaign", new RenameRequest("Archive/Campaign"), CancellationToken.None);

        var entry = await _db.AuditLog.SingleAsync(a => a.Action == "Folder.Rename" || a.Action == "Folder.Move");
        Assert.Equal("Folder.Move", entry.Action);
    }

    [Fact]
    public async Task RenameFolder_DestinationAlreadyExists_ReturnsConflict()
    {
        await _sut.CreateFolder("Campaign", CancellationToken.None);
        await _sut.CreateFolder("Existing", CancellationToken.None);

        var result = await _sut.RenameFolder("Campaign", new RenameRequest("Existing"), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.True(Directory.Exists(Path.Combine(_hubRoot, "Campaign"))); // untouched
    }

    [Fact]
    public async Task RenameFolder_IntoItself_ReturnsBadRequest()
    {
        await _sut.CreateFolder("Campaign", CancellationToken.None);

        var result = await _sut.RenameFolder("Campaign", new RenameRequest("Campaign/Sub"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task RenameFolder_UserWithoutManagePermission_ReturnsForbid()
    {
        await _sut.CreateFolder("Campaign", CancellationToken.None);
        var plainUser = new AppUser { Username = "plain2", NormalizedUsername = "PLAIN2", IsAdministrator = false };
        _db.Users.Add(plainUser);
        _db.SaveChanges();
        _db.FolderPermissions.Add(new FolderPermission { AppUserId = plainUser.Id, FolderPath = "Campaign", Level = PermissionLevel.Edit });
        await _db.SaveChangesAsync();
        var controller = BuildController(plainUser);

        var result = await controller.RenameFolder("Campaign", new RenameRequest("Old Campaign"), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.True(Directory.Exists(Path.Combine(_hubRoot, "Campaign")));
    }

    [Fact]
    public async Task DeleteFolder_RemovesTheDirectoryFromDisk()
    {
        await _sut.CreateFolder("Campaign", CancellationToken.None);

        var result = await _sut.DeleteFolder("Campaign", CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.False(Directory.Exists(Path.Combine(_hubRoot, "Campaign")));
    }

    [Fact]
    public async Task DeleteFolder_SoftDeletesEveryContainedDocument_PreservingItsHistory()
    {
        await _sut.SavePage("Campaign/Session 1.md", new SavePageRequest("Notes", null), CancellationToken.None);
        await _sut.SavePage("Campaign/Sessions/Session 2.md", new SavePageRequest("More notes", null), CancellationToken.None);
        var session1Id = (await _db.Pages.SingleAsync(p => p.RelativePath == "Campaign/Session 1.md")).Id;
        var session2Id = (await _db.Pages.SingleAsync(p => p.RelativePath == "Campaign/Sessions/Session 2.md")).Id;

        await _sut.DeleteFolder("Campaign", CancellationToken.None);

        var session1 = await _db.Pages.SingleAsync(p => p.Id == session1Id);
        var session2 = await _db.Pages.SingleAsync(p => p.Id == session2Id);
        Assert.True(session1.IsDeleted);
        Assert.True(session2.IsDeleted);
        // Rows (and their version history) are preserved, not hard-removed.
        Assert.Equal("Campaign/Session 1.md", session1.RelativePath);
        Assert.Equal("Campaign/Sessions/Session 2.md", session2.RelativePath);
    }

    [Fact]
    public async Task DeleteFolder_LeavesSiblingDocumentsUntouched()
    {
        await _sut.SavePage("Campaign/Session 1.md", new SavePageRequest("Notes", null), CancellationToken.None);
        await _sut.SavePage("Other/Notes.md", new SavePageRequest("Unrelated", null), CancellationToken.None);

        await _sut.DeleteFolder("Campaign", CancellationToken.None);

        var other = await _db.Pages.SingleAsync(p => p.RelativePath == "Other/Notes.md");
        Assert.False(other.IsDeleted);
        Assert.True(Directory.Exists(Path.Combine(_hubRoot, "Other")));
    }

    [Fact]
    public async Task DeleteFolder_LogsFolderDelete()
    {
        await _sut.SavePage("Campaign/Session 1.md", new SavePageRequest("Notes", null), CancellationToken.None);

        await _sut.DeleteFolder("Campaign", CancellationToken.None);

        var entry = await _db.AuditLog.SingleAsync(a => a.Action == "Folder.Delete");
        Assert.Equal("Folder", entry.ObjectType);
        Assert.Equal("Campaign", entry.TargetPath);
    }

    [Fact]
    public async Task DeleteFolder_NonExistentFolder_ReturnsNotFound()
    {
        var result = await _sut.DeleteFolder("DoesNotExist", CancellationToken.None);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task DeleteFolder_UserWithoutManagePermission_ReturnsForbid()
    {
        await _sut.CreateFolder("Campaign", CancellationToken.None);
        var plainUser = new AppUser { Username = "plain3", NormalizedUsername = "PLAIN3", IsAdministrator = false };
        _db.Users.Add(plainUser);
        _db.SaveChanges();
        _db.FolderPermissions.Add(new FolderPermission { AppUserId = plainUser.Id, FolderPath = "Campaign", Level = PermissionLevel.Edit });
        await _db.SaveChangesAsync();
        var controller = BuildController(plainUser);

        var result = await controller.DeleteFolder("Campaign", CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.True(Directory.Exists(Path.Combine(_hubRoot, "Campaign")));
    }
}
