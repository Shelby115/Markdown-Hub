using System.Security.Claims;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Controllers.Auth;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

public class AuthProvidersControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuthProvidersController _sut;
    private readonly AppUser _admin;

    public AuthProvidersControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _admin = new AppUser { Username = "admin", NormalizedUsername = "ADMIN", IsAdministrator = true };
        _db.Users.Add(_admin);
        _db.SaveChanges();

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
        var safety = new AccountSafetyService(_db);
        var secretProtector = new ProviderSecretProtector(new EphemeralDataProtectionProvider());
        _sut = new AuthProvidersController(_db, currentUser, audit, safety, secretProtector);
    }

    public void Dispose() => _db.Dispose();

    private static ProviderConfiguration OidcConfig(string authority = "https://auth.example.com/realms/markdown-hub") =>
        new() { Authority = authority, RequireHttpsMetadata = true };

    private static CreateAuthenticationProviderRequest ValidCreateRequest(string name = "Keycloak") =>
        new(name, name, AuthProviderType.Oidc, "markdown-hub", "s3cr3t", OidcConfig());

    [Fact]
    public async Task Create_PersistsProviderAndReturnsIt()
    {
        var result = await _sut.Create(ValidCreateRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AuthenticationProviderResponse>(ok.Value);
        Assert.Equal("Keycloak", response.DisplayName);
        Assert.True(response.Enabled);
        Assert.True(response.HasClientSecret);
        Assert.Single(_db.AuthenticationProviders);
    }

    [Fact]
    public async Task Create_RecordsAuditEntry()
    {
        await _sut.Create(ValidCreateRequest(), CancellationToken.None);

        var entry = Assert.Single(_db.AuditLog);
        Assert.Equal("Auth.ProviderCreated", entry.Action);
        Assert.Equal(_admin.Id, entry.AppUserId);
    }

    [Fact]
    public async Task Create_DuplicateName_ReturnsConflict()
    {
        await _sut.Create(ValidCreateRequest(), CancellationToken.None);

        var result = await _sut.Create(ValidCreateRequest(), CancellationToken.None);

        Assert.IsType<ConflictObjectResult>(result);
        Assert.Single(_db.AuthenticationProviders);
    }

    [Theory]
    [InlineData("", "Keycloak", "client")]
    [InlineData("Name", "", "client")]
    [InlineData("Name", "Keycloak", "")]
    public async Task Create_InvalidInput_ReturnsBadRequestAndDoesNotPersist(string name, string displayName, string clientId)
    {
        var result = await _sut.Create(
            new CreateAuthenticationProviderRequest(name, displayName, AuthProviderType.Oidc, clientId, "secret", OidcConfig()),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Empty(_db.AuthenticationProviders);
    }

    [Fact]
    public async Task Create_OidcWithoutAuthority_ReturnsBadRequest()
    {
        var result = await _sut.Create(
            new CreateAuthenticationProviderRequest("kc", "Keycloak", AuthProviderType.Oidc, "client", "secret", new ProviderConfiguration()),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Create_OAuth2WithoutEndpoints_ReturnsBadRequest()
    {
        var result = await _sut.Create(
            new CreateAuthenticationProviderRequest("gh", "GitHub", AuthProviderType.OAuth2, "client", "secret", new ProviderConfiguration()),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Update_ChangesFieldsAndLeavesSecretUnchangedWhenBlank()
    {
        var created = Assert.IsType<AuthenticationProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidCreateRequest(), CancellationToken.None)).Value);

        var result = await _sut.Update(created.Id,
            new UpdateAuthenticationProviderRequest("Renamed", AuthProviderType.Oidc, "new-client", null, OidcConfig("https://auth2.example.com")),
            CancellationToken.None);

        var response = Assert.IsType<AuthenticationProviderResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("Renamed", response.DisplayName);
        Assert.Equal("new-client", response.ClientId);
        Assert.Equal("https://auth2.example.com", response.Configuration.Authority);
        Assert.True(response.HasClientSecret);
    }

    [Fact]
    public async Task Delete_ProviderNotUsedByAnyAdmin_Succeeds()
    {
        var created = Assert.IsType<AuthenticationProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidCreateRequest(), CancellationToken.None)).Value);

        var result = await _sut.Delete(created.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
        Assert.Empty(_db.AuthenticationProviders);
    }

    /// <summary>Auth.md §10's own example: an administrator with both a local password AND
    /// Keycloak linked may remove Keycloak, since the password remains as a usable method - this
    /// replaces the old blanket "can't touch the last enabled provider" rule, which would have
    /// wrongly refused this.</summary>
    [Fact]
    public async Task Delete_AdminHasPasswordAndThisProvider_Succeeds()
    {
        var created = Assert.IsType<AuthenticationProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidCreateRequest(), CancellationToken.None)).Value);
        _admin.PasswordHash = "some-hash";
        _db.AuthenticationIdentities.Add(new AuthenticationIdentity { UserId = _admin.Id, AuthenticationProviderId = created.Id, Subject = "admin-sub" });
        await _db.SaveChangesAsync();

        var result = await _sut.Delete(created.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    /// <summary>The other half of Auth.md §10's example: an administrator with ONLY Keycloak (no
    /// password, no other linked provider) must not be able to have it removed out from under
    /// them - that's their sole authentication method.</summary>
    [Fact]
    public async Task Delete_ProviderIsSoleAdminsSoleAuthMethod_IsRefused()
    {
        var created = Assert.IsType<AuthenticationProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidCreateRequest(), CancellationToken.None)).Value);
        _db.AuthenticationIdentities.Add(new AuthenticationIdentity { UserId = _admin.Id, AuthenticationProviderId = created.Id, Subject = "admin-sub" });
        await _db.SaveChangesAsync();

        var result = await _sut.Delete(created.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Single(_db.AuthenticationProviders);
    }

    [Fact]
    public async Task Delete_WithSecondAdministrator_Succeeds()
    {
        var created = Assert.IsType<AuthenticationProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidCreateRequest(), CancellationToken.None)).Value);
        _db.AuthenticationIdentities.Add(new AuthenticationIdentity { UserId = _admin.Id, AuthenticationProviderId = created.Id, Subject = "admin-sub" });
        _db.Users.Add(new AppUser { Username = "admin2", NormalizedUsername = "ADMIN2", IsAdministrator = true, PasswordHash = "hash" });
        await _db.SaveChangesAsync();

        var result = await _sut.Delete(created.Id, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    [Fact]
    public async Task Disable_ProviderIsSoleAdminsSoleAuthMethod_IsRefused()
    {
        var created = Assert.IsType<AuthenticationProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidCreateRequest(), CancellationToken.None)).Value);
        _db.AuthenticationIdentities.Add(new AuthenticationIdentity { UserId = _admin.Id, AuthenticationProviderId = created.Id, Subject = "admin-sub" });
        await _db.SaveChangesAsync();

        var result = await _sut.Disable(created.Id, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.True((await _db.AuthenticationProviders.FindAsync(created.Id))!.Enabled);
    }

    [Fact]
    public async Task List_ReturnsAllProvidersRegardlessOfEnabledState()
    {
        await _sut.Create(ValidCreateRequest("first"), CancellationToken.None);
        var second = Assert.IsType<AuthenticationProviderResponse>(Assert.IsType<OkObjectResult>(
            await _sut.Create(ValidCreateRequest("second"), CancellationToken.None)).Value);
        await _sut.Disable(second.Id, CancellationToken.None);

        var result = Assert.IsType<OkObjectResult>(await _sut.List(CancellationToken.None));
        var providers = Assert.IsAssignableFrom<IEnumerable<AuthenticationProviderResponse>>(result.Value);
        Assert.Equal(2, providers.Count());
    }

    [Fact]
    public void Presets_ReturnsNonEmptyList()
    {
        var result = Assert.IsType<OkObjectResult>(_sut.Presets());
        var presets = Assert.IsAssignableFrom<IEnumerable<ProviderPresetResponse>>(result.Value);
        Assert.NotEmpty(presets);
        Assert.Contains(presets, p => p.Key == "google");
    }
}
