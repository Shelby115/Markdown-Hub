using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Controllers.Auth;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

public class AccountControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AccountController _sut;
    private readonly AppUser _user;
    private readonly IPasswordHasher<AppUser> _hasher = new PasswordHasher<AppUser>();

    public AccountControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _user = new AppUser { Username = "gm", NormalizedUsername = "GM" };
        _db.Users.Add(_user);
        _db.SaveChanges();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString()),
                    new Claim("preferred_username", _user.Username),
                ]))
            }
        };
        var currentUser = new CurrentUserService(_db, httpContextAccessor);
        var audit = new AuditLogService(_db, httpContextAccessor);
        var safety = new AccountSafetyService(_db);
        _sut = new AccountController(currentUser, _db, audit, _hasher, safety)
        {
            // AccountController reads claims via ControllerBase.User (HttpContext.User), not via the
            // separately-constructed IHttpContextAccessor above - both must point at the same
            // HttpContext for CurrentSessionId to resolve during a test.
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContextAccessor.HttpContext! },
        };
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task SetDefaultFolder_TrimsSlashesAndPersists()
    {
        await _sut.SetDefaultFolder(new SetDefaultFolderRequest("/Campaigns/Campaign 1/Sessions/"), CancellationToken.None);

        var reloaded = await _db.Users.FirstAsync(u => u.Id == _user.Id);
        Assert.Equal("Campaigns/Campaign 1/Sessions", reloaded.DefaultFolderPath);
    }

    [Fact]
    public async Task SetDefaultFolder_EmptyStringClearsIt()
    {
        _user.DefaultFolderPath = "Somewhere";
        await _db.SaveChangesAsync();

        await _sut.SetDefaultFolder(new SetDefaultFolderRequest(""), CancellationToken.None);

        var reloaded = await _db.Users.FirstAsync(u => u.Id == _user.Id);
        Assert.Null(reloaded.DefaultFolderPath);
    }

    [Fact]
    public async Task Get_ReturnsDefaultFolderPath()
    {
        _user.DefaultFolderPath = "Campaigns/Campaign 1";
        await _db.SaveChangesAsync();

        var result = await _sut.Get(CancellationToken.None);

        var value = Assert.IsAssignableFrom<Microsoft.AspNetCore.Mvc.OkObjectResult>(result).Value;
        var prop = value!.GetType().GetProperty("DefaultFolderPath");
        Assert.Equal("Campaigns/Campaign 1", prop!.GetValue(value));
    }

    [Fact]
    public async Task ChangePassword_NoExistingPassword_DoesNotRequireCurrentPassword()
    {
        var result = await _sut.ChangePassword(new ChangePasswordRequest(null, "newpassword1", "newpassword1"), CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Mvc.NoContentResult>(result);
        var reloaded = await _db.Users.FirstAsync(u => u.Id == _user.Id);
        Assert.NotNull(reloaded.PasswordHash);
    }

    [Fact]
    public async Task ChangePassword_WrongCurrentPassword_IsRejected()
    {
        _user.PasswordHash = _hasher.HashPassword(_user, "correct-password");
        await _db.SaveChangesAsync();

        var result = await _sut.ChangePassword(new ChangePasswordRequest("wrong-password", "newpassword1", "newpassword1"), CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ChangePassword_MismatchedConfirmation_IsRejected()
    {
        var result = await _sut.ChangePassword(new ChangePasswordRequest(null, "newpassword1", "different"), CancellationToken.None);

        Assert.IsType<Microsoft.AspNetCore.Mvc.BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task ChangePassword_RevokesOtherSessionsButNotCurrent()
    {
        _user.PasswordHash = _hasher.HashPassword(_user, "correct-password");
        var currentSession = new Session { UserId = _user.Id, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        var otherSession = new Session { UserId = _user.Id, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1) };
        _db.Sessions.AddRange(currentSession, otherSession);
        await _db.SaveChangesAsync();

        // Rebuild the principal to include the "current" session id, same as a real request would.
        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString()),
                    new Claim("sid", currentSession.Id.ToString()),
                ]))
            }
        };
        var currentUser = new CurrentUserService(_db, httpContextAccessor);
        var audit = new AuditLogService(_db, httpContextAccessor);
        var safety = new AccountSafetyService(_db);
        var sut = new AccountController(currentUser, _db, audit, _hasher, safety)
        {
            ControllerContext = new Microsoft.AspNetCore.Mvc.ControllerContext { HttpContext = httpContextAccessor.HttpContext! },
        };

        await sut.ChangePassword(new ChangePasswordRequest("correct-password", "newpassword1", "newpassword1"), CancellationToken.None);

        var reloadedCurrent = await _db.Sessions.FirstAsync(s => s.Id == currentSession.Id);
        var reloadedOther = await _db.Sessions.FirstAsync(s => s.Id == otherSession.Id);
        Assert.Null(reloadedCurrent.RevokedAt);
        Assert.NotNull(reloadedOther.RevokedAt);
    }
}
