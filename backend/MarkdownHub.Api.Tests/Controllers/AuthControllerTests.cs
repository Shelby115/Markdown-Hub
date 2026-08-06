using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Controllers.Auth;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

public class AuthControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuthController _sut;
    private readonly IPasswordHasher<AppUser> _hasher = new PasswordHasher<AppUser>();

    public AuthControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var httpContextAccessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext() };
        var audit = new AuditLogService(_db, httpContextAccessor);
        var tokens = new AppTokenService(_db, new ConfigurationBuilder().Build(), httpContextAccessor);
        var external = new ExternalAuthService(
            new FakeHttpClientFactory(),
            new ProviderSecretProtector(new EphemeralDataProtectionProvider()),
            new EphemeralDataProtectionProvider());
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "http://localhost:8086" })
            .Build();
        _sut = new AuthController(_db, _hasher, tokens, external, audit, config, Microsoft.Extensions.Logging.Abstractions.NullLogger<AuthController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() },
        };
    }

    public void Dispose() => _db.Dispose();

    private class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private async Task<AppUser> CreateLocalUserAsync(string username, string password, bool disabled = false)
    {
        var user = new AppUser { Username = username, NormalizedUsername = AppUser.Normalize(username), IsDisabled = disabled };
        user.PasswordHash = _hasher.HashPassword(user, password);
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task Login_ValidCredentials_IssuesTokenAndSession()
    {
        await CreateLocalUserAsync("alice", "correct horse battery staple");

        var result = await _sut.Login(new LoginRequest("alice", "correct horse battery staple"), CancellationToken.None);

        var response = Assert.IsType<LoginResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(string.IsNullOrEmpty(response.Token));
        Assert.Single(_db.Sessions);
    }

    [Fact]
    public async Task Login_ValidCredentials_LogsAuthLogin()
    {
        var user = await CreateLocalUserAsync("alice", "correct horse battery staple");

        await _sut.Login(new LoginRequest("alice", "correct horse battery staple"), CancellationToken.None);

        var entry = Assert.Single(_db.AuditLog);
        Assert.Equal("Auth.Login", entry.Action);
        Assert.Equal(user.Id, entry.AppUserId);
    }

    [Fact]
    public async Task Login_WrongPassword_ReturnsUnauthorizedWithGenericMessage()
    {
        await CreateLocalUserAsync("alice", "correct horse battery staple");

        var result = await _sut.Login(new LoginRequest("alice", "wrong-password"), CancellationToken.None);

        var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
        var message = unauthorized.Value!.GetType().GetProperty("message")!.GetValue(unauthorized.Value) as string;
        Assert.Equal("Invalid username or password.", message);
        Assert.Empty(_db.Sessions);
    }

    [Fact]
    public async Task Login_UnknownUsername_ReturnsSameGenericMessageAsWrongPassword()
    {
        // No user-enumeration: an unknown username and a wrong password must be indistinguishable.
        var unknownResult = await _sut.Login(new LoginRequest("nobody", "whatever"), CancellationToken.None);
        await CreateLocalUserAsync("alice", "correct horse battery staple");
        var wrongPasswordResult = await _sut.Login(new LoginRequest("alice", "wrong-password"), CancellationToken.None);

        var unknownMessage = (string)Assert.IsType<UnauthorizedObjectResult>(unknownResult).Value!.GetType()
            .GetProperty("message")!.GetValue(Assert.IsType<UnauthorizedObjectResult>(unknownResult).Value)!;
        var wrongMessage = (string)Assert.IsType<UnauthorizedObjectResult>(wrongPasswordResult).Value!.GetType()
            .GetProperty("message")!.GetValue(Assert.IsType<UnauthorizedObjectResult>(wrongPasswordResult).Value)!;
        Assert.Equal(unknownMessage, wrongMessage);
    }

    [Fact]
    public async Task Login_DisabledUser_ReturnsUnauthorized()
    {
        await CreateLocalUserAsync("alice", "correct horse battery staple", disabled: true);

        var result = await _sut.Login(new LoginRequest("alice", "correct horse battery staple"), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Login_UserWithNoPassword_ReturnsUnauthorized()
    {
        // e.g. an external-provider-only account, or an admin-pre-provisioned placeholder that
        // hasn't been given a temporary password yet.
        _db.Users.Add(new AppUser { Username = "external-only", NormalizedUsername = "EXTERNAL-ONLY" });
        await _db.SaveChangesAsync();

        var result = await _sut.Login(new LoginRequest("external-only", "anything"), CancellationToken.None);

        Assert.IsType<UnauthorizedObjectResult>(result);
    }

    [Fact]
    public async Task Login_IsCaseInsensitiveOnUsername()
    {
        await CreateLocalUserAsync("Alice", "correct horse battery staple");

        var result = await _sut.Login(new LoginRequest("ALICE", "correct horse battery staple"), CancellationToken.None);

        Assert.IsType<OkObjectResult>(result);
    }
}
