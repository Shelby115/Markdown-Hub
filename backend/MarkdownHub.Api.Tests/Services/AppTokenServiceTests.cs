using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class AppTokenServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AppTokenService _sut;

    public AppTokenServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        _sut = new AppTokenService(_db, new ConfigurationBuilder().Build(), httpContextAccessor);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetSigningKeyAsync_GeneratesAndPersistsAKeyOnce()
    {
        var key1 = await _sut.GetSigningKeyAsync();
        var key2 = await _sut.GetSigningKeyAsync();

        Assert.Equal(32, key1.Length); // 256-bit
        Assert.Equal(key1, key2); // same key reused, not regenerated per call
        Assert.Single(_db.Settings);
    }

    [Fact]
    public async Task GetSigningKeyAsync_ConfiguredOverrideTakesPrecedence()
    {
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = "a-configured-override-key" })
            .Build();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var sut = new AppTokenService(_db, configured, httpContextAccessor);

        await sut.GetSigningKeyAsync();

        Assert.Empty(_db.Settings); // never persisted - the override is used directly every time
    }

    [Fact]
    public async Task IssueAsync_CreatesASessionAndAJwtCarryingItsId()
    {
        var user = new AppUser { Username = "alice", NormalizedUsername = "ALICE" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (token, session) = await _sut.IssueAsync(user);

        Assert.Single(_db.Sessions);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(user.Id.ToString(), jwt.Subject);
        Assert.Equal(session.Id.ToString(), jwt.Claims.First(c => c.Type == "sid").Value);
        Assert.Equal(AppTokenService.Issuer, jwt.Issuer);
    }

    [Fact]
    public async Task IssueAsync_SessionExpiryMatchesConfiguredLifetime()
    {
        var configured = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Sessions:LifetimeHours"] = "1" })
            .Build();
        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var sut = new AppTokenService(_db, configured, httpContextAccessor);
        var user = new AppUser { Username = "alice", NormalizedUsername = "ALICE" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var (_, session) = await sut.IssueAsync(user);

        var delta = session.ExpiresAt - DateTimeOffset.UtcNow;
        Assert.True(delta > TimeSpan.FromMinutes(55) && delta <= TimeSpan.FromMinutes(60));
    }
}
