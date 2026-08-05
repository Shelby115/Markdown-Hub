using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Controllers;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

public class OidcProvidersControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly OidcProvidersController _sut;
    private readonly AppUser _admin;

    public OidcProvidersControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _admin = new AppUser { KeycloakSubjectId = "admin-sub", Username = "admin", IsAdministrator = true };
        _db.Users.Add(_admin);
        _db.SaveChanges();

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
        _sut = new OidcProvidersController(_db, currentUser, new AuditLogService(_db, httpContextAccessor));
    }

    public void Dispose() => _db.Dispose();

    private static SaveOidcProviderRequest ValidRequest(string name = "Keycloak") =>
        new(name, "https://auth.example.com/realms/markdown-hub", "markdown-hub-spa", "markdown-hub-api", true);

    [Fact]
    public async Task Create_PersistsProviderAndReturnsIt()
    {
        var result = await _sut.Create(ValidRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<OidcProviderResponse>(ok.Value);
        Assert.Equal("Keycloak", response.Name);
        Assert.True(response.IsEnabled);
        Assert.Single(_db.OidcProviders);
    }

    [Fact]
    public async Task Create_RecordsAuditEntry()
    {
        await _sut.Create(ValidRequest(), CancellationToken.None);

        var entry = Assert.Single(_db.AuditLog);
        Assert.Equal("OidcProvider.Create", entry.Action);
        Assert.Equal(_admin.Id, entry.AppUserId);
    }

    [Theory]
    [InlineData("", "https://auth.example.com", "client", "aud")]
    [InlineData("Name", "not-a-url", "client", "aud")]
    [InlineData("Name", "https://auth.example.com", "", "aud")]
    [InlineData("Name", "https://auth.example.com", "client", "")]
    public async Task Create_InvalidInput_ReturnsBadRequestAndDoesNotPersist(string name, string authority, string clientId, string audience)
    {
        var result = await _sut.Create(new SaveOidcProviderRequest(name, authority, clientId, audience, true), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(_db.OidcProviders);
    }

    [Fact]
    public async Task Update_ChangesFields()
    {
        var created = Assert.IsType<OidcProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidRequest(), CancellationToken.None)).Value);

        var result = await _sut.Update(created.Id,
            new SaveOidcProviderRequest("Renamed", "https://auth2.example.com/realms/x", "new-client", "new-aud", false),
            CancellationToken.None);

        var response = Assert.IsType<OidcProviderResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("Renamed", response.Name);
        Assert.Equal("new-client", response.ClientId);
        Assert.False(response.RequireHttpsMetadata);
    }

    [Fact]
    public async Task Delete_LastEnabledProvider_IsRefused()
    {
        var created = Assert.IsType<OidcProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidRequest(), CancellationToken.None)).Value);

        var result = await _sut.Delete(created.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Single(_db.OidcProviders);
    }

    [Fact]
    public async Task Delete_WithAnotherEnabledProviderRemaining_Succeeds()
    {
        var first = Assert.IsType<OidcProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidRequest("First"), CancellationToken.None)).Value);
        await _sut.Create(ValidRequest("Second"), CancellationToken.None);

        var result = await _sut.Delete(first.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Single(_db.OidcProviders);
    }

    [Fact]
    public async Task Disable_LastEnabledProvider_IsRefused()
    {
        var created = Assert.IsType<OidcProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidRequest(), CancellationToken.None)).Value);

        var result = await _sut.Disable(created.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.True((await _db.OidcProviders.FindAsync(created.Id))!.IsEnabled);
    }

    [Fact]
    public async Task Disable_WithAnotherEnabledProviderRemaining_Succeeds()
    {
        var first = Assert.IsType<OidcProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidRequest("First"), CancellationToken.None)).Value);
        await _sut.Create(ValidRequest("Second"), CancellationToken.None);

        var result = await _sut.Disable(first.Id, CancellationToken.None);

        var response = Assert.IsType<OidcProviderResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.False(response.IsEnabled);
    }

    [Fact]
    public async Task List_ReturnsAllProvidersRegardlessOfEnabledState()
    {
        await _sut.Create(ValidRequest("First"), CancellationToken.None);
        await _sut.Create(ValidRequest("Second"), CancellationToken.None);
        var third = Assert.IsType<OidcProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidRequest("Third"), CancellationToken.None)).Value);
        await _sut.Disable(third.Id, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(await _sut.List(CancellationToken.None));
        var providers = Assert.IsAssignableFrom<IEnumerable<OidcProviderResponse>>(result.Value);
        Assert.Equal(3, providers.Count());
    }
}
