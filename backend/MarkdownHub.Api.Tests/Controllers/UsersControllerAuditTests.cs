using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Controllers.Auth;
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

        _admin = new AppUser { Username = "admin", NormalizedUsername = "ADMIN", IsAdministrator = true };
        _db.Users.Add(_admin);
        _db.SaveChanges();

        // CurrentUserService resolves the "acting user" from the request's JWT claims - set up an
        // HttpContext carrying the seeded admin's id so LogAsync attributes entries to them.
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, _admin.Id.ToString()),
                    new Claim("preferred_username", _admin.Username),
                ]))
            }
        };
        var currentUser = new CurrentUserService(_db, httpContextAccessor);
        var audit = new AuditLogService(_db, httpContextAccessor);
        var hasher = new PasswordHasher<AppUser>();
        var safety = new AccountSafetyService(_db);
        _sut = new UsersController(_db, currentUser, audit, hasher, safety);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task PromoteToAdmin_RecordsAuditEntryAttributedToActingAdmin()
    {
        var target = new AppUser { Username = "bob", NormalizedUsername = "BOB" };
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
        var target = new AppUser { Username = "carol", NormalizedUsername = "CAROL" };
        _db.Users.Add(target);
        await _db.SaveChangesAsync();

        await _sut.DeleteUser(target.Id, CancellationToken.None);

        var entry = Assert.Single(_db.AuditLog);
        Assert.Equal("User.Delete", entry.Action);
        Assert.Equal("carol", entry.TargetPath);
    }

    [Fact]
    public async Task DeleteUser_LastAdministrator_IsRefused()
    {
        var result = await _sut.DeleteUser(_admin.Id, CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(result);
        Assert.NotNull(await _db.Users.FindAsync(_admin.Id));
    }

    [Fact]
    public async Task GrantPermission_RecordsAuditEntryWithFolderAndLevel()
    {
        var target = new AppUser { Username = "dave", NormalizedUsername = "DAVE" };
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
        _db.Users.Add(new AppUser { Username = "erin", NormalizedUsername = "ERIN" });
        await _db.SaveChangesAsync();

        await _sut.CreateUser(new CreateUserRequest("erin", null), CancellationToken.None);

        Assert.Empty(_db.AuditLog);
    }

    [Fact]
    public async Task CreateUser_WithoutTemporaryPassword_GeneratesOneAndHashesIt()
    {
        var result = await _sut.CreateUser(new CreateUserRequest("frank", null), CancellationToken.None);

        var response = Assert.IsType<CreateUserResponse>(Assert.IsType<Microsoft.AspNetCore.Mvc.OkObjectResult>(result).Value);
        Assert.False(string.IsNullOrEmpty(response.TemporaryPassword));
        var created = await _db.Users.FirstAsync(u => u.Username == "frank");
        Assert.NotNull(created.PasswordHash);
    }
}
