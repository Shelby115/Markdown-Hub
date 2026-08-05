using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Tests.Data;

public class StartupSeederTests : IDisposable
{
    private readonly AppDbContext _db;

    public StartupSeederTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
    }

    public void Dispose() => _db.Dispose();

    private static IConfiguration Config(Dictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();

    [Fact]
    public async Task SeedAdminAsync_NoSeedUsernameConfigured_DoesNothing()
    {
        await StartupSeeder.SeedAdminAsync(_db, Config(new()));

        Assert.Empty(_db.Users);
    }

    [Fact]
    public async Task SeedAdminAsync_CreatesPendingAdministratorUser()
    {
        await StartupSeeder.SeedAdminAsync(_db, Config(new() { ["Admin:SeedUsername"] = "alice" }));

        var user = Assert.Single(_db.Users);
        Assert.Equal("alice", user.Username);
        Assert.True(user.IsAdministrator);
        Assert.True(user.IsPending);
    }

    [Fact]
    public async Task SeedAdminAsync_UsernameAlreadyExists_DoesNotCreateOrModifyIt()
    {
        _db.Users.Add(new AppUser { KeycloakSubjectId = "real-sub", Username = "alice", IsAdministrator = false });
        await _db.SaveChangesAsync();

        await StartupSeeder.SeedAdminAsync(_db, Config(new() { ["Admin:SeedUsername"] = "alice" }));

        var user = Assert.Single(_db.Users);
        Assert.False(user.IsAdministrator); // never silently promoted - create-only semantics
    }

    [Fact]
    public async Task SeedDefaultOidcProviderAsync_ProvidersAlreadyExist_DoesNothing()
    {
        _db.OidcProviders.Add(new OidcProvider { Name = "Existing", Authority = "https://existing", ClientId = "c", Audience = "a" });
        await _db.SaveChangesAsync();

        await StartupSeeder.SeedDefaultOidcProviderAsync(_db, Config(new()
        {
            ["OidcDefault:Authority"] = "https://new",
            ["OidcDefault:ClientId"] = "new-client",
            ["OidcDefault:Audience"] = "new-aud",
        }));

        var provider = Assert.Single(_db.OidcProviders);
        Assert.Equal("Existing", provider.Name);
    }

    [Fact]
    public async Task SeedDefaultOidcProviderAsync_NothingConfigured_DoesNotCreateAProvider()
    {
        await StartupSeeder.SeedDefaultOidcProviderAsync(_db, Config(new()));

        Assert.Empty(_db.OidcProviders);
    }

    [Fact]
    public async Task SeedDefaultOidcProviderAsync_ConfiguredValues_CreatesEnabledProvider()
    {
        await StartupSeeder.SeedDefaultOidcProviderAsync(_db, Config(new()
        {
            ["OidcDefault:Name"] = "Keycloak",
            ["OidcDefault:Authority"] = "https://auth.example.com/realms/markdown-hub",
            ["OidcDefault:ClientId"] = "markdown-hub-spa",
            ["OidcDefault:Audience"] = "markdown-hub-api",
            ["OidcDefault:RequireHttpsMetadata"] = "true",
        }));

        var provider = Assert.Single(_db.OidcProviders);
        Assert.Equal("Keycloak", provider.Name);
        Assert.Equal("https://auth.example.com/realms/markdown-hub", provider.Authority);
        Assert.True(provider.IsEnabled);
        Assert.True(provider.RequireHttpsMetadata);
    }
}
