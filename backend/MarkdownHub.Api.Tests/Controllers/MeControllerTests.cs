using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Controllers;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

public class MeControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly MeController _sut;
    private readonly AppUser _user;

    public MeControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _user = new AppUser { KeycloakSubjectId = "sub-1", Username = "gm" };
        _db.Users.Add(_user);
        _db.SaveChanges();

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, _user.KeycloakSubjectId),
                    new Claim("preferred_username", _user.Username),
                ]))
            }
        };
        var currentUser = new CurrentUserService(_db, httpContextAccessor);
        var audit = new AuditLogService(_db, httpContextAccessor);
        _sut = new MeController(currentUser, _db, audit);
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
}
