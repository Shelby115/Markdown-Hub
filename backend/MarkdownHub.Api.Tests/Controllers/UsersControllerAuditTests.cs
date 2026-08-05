using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Controllers;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

public class UsersControllerAuditTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly UsersController _sut;
    private readonly AppUser _admin;

    public UsersControllerAuditTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _admin = new AppUser { KeycloakSubjectId = "admin-sub", Username = "admin", IsAdministrator = true };
        _db.Users.Add(_admin);
        _db.SaveChanges();

        // CurrentUserService resolves the "acting user" from the request's JWT claims - set up an
        // HttpContext carrying the seeded admin's subject so LogAsync attributes entries to them.
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, _admin.KeycloakSubjectId),
                    new Claim("preferred_username", _admin.Username),
                ]))
            }
        };
        var currentUser = new CurrentUserService(_db, httpContextAccessor);
        var audit = new AuditLogService(_db, httpContextAccessor);
        _sut = new UsersController(_db, currentUser, audit);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task PromoteToAdmin_RecordsAuditEntryAttributedToActingAdmin()
    {
        var target = new AppUser { KeycloakSubjectId = "target-sub", Username = "bob" };
        _db.Users.Add(target);
        await _db.SaveChangesAsync();

        await _sut.PromoteToAdmin(target.Id, CancellationToken.None);

        var entry = Assert.Single(_db.AuditLog);
        Assert.Equal("User.Promote", entry.Action);
        Assert.Equal("bob", entry.TargetPath);
        Assert.Equal(_admin.Id, entry.AppUserId);
    }

    [Fact]
    public async Task DeleteUser_RecordsAuditEntry()
    {
        var target = new AppUser { KeycloakSubjectId = "target-sub-2", Username = "carol" };
        _db.Users.Add(target);
        await _db.SaveChangesAsync();

        await _sut.DeleteUser(target.Id, CancellationToken.None);

        var entry = Assert.Single(_db.AuditLog);
        Assert.Equal("User.Delete", entry.Action);
        Assert.Equal("carol", entry.TargetPath);
    }

    [Fact]
    public async Task GrantPermission_RecordsAuditEntryWithFolderAndLevel()
    {
        var target = new AppUser { KeycloakSubjectId = "target-sub-3", Username = "dave" };
        _db.Users.Add(target);
        await _db.SaveChangesAsync();

        await _sut.GrantPermission(new GrantPermissionRequest(target.Id, "/Projects/", PermissionLevel.Edit), CancellationToken.None);

        var entry = Assert.Single(_db.AuditLog);
        Assert.Equal("Permission.Grant", entry.Action);
        Assert.Equal("Projects", entry.TargetPath); // leading/trailing slashes trimmed
        Assert.Contains("Edit", entry.Details);
    }

    [Fact]
    public async Task CreateUser_DoesNotRecordAuditEntryOnFailure()
    {
        // A duplicate username is rejected before any change is made - no audit entry should
        // appear for an action that never actually happened.
        _db.Users.Add(new AppUser { KeycloakSubjectId = "existing-sub", Username = "erin" });
        await _db.SaveChangesAsync();

        await _sut.CreateUser(new CreateUserRequest("erin"), CancellationToken.None);

        Assert.Empty(_db.AuditLog);
    }
}
