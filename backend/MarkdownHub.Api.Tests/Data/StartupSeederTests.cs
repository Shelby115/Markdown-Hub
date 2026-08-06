using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Data;

public class StartupSeederTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher = new PasswordHasher<AppUser>();
    private readonly ProviderSecretProtector _secretProtector = new(new EphemeralDataProtectionProvider());

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
    public async Task SeedAdminAsync_NoUsernameConfigured_DoesNothing()
    {
        await StartupSeeder.SeedAdminAsync(_db, Config(new()), _hasher);

        Assert.Empty(_db.Users);
    }

    [Fact]
    public async Task SeedAdminAsync_UsernameWithoutPassword_DoesNothing()
    {
        // Auth.md §3: the initial administrator account must always have a password - seeding a
        // passwordless placeholder that might never get a password risks a permanently
        // unusable reserved username.
        await StartupSeeder.SeedAdminAsync(_db, Config(new() { ["Admin:Username"] = "alice" }), _hasher);

        Assert.Empty(_db.Users);
    }

    [Fact]
    public async Task SeedAdminAsync_UsernameAndPassword_CreatesHashedAdministrator()
    {
        await StartupSeeder.SeedAdminAsync(_db, Config(new()
        {
            ["Admin:Username"] = "alice",
            ["Admin:Password"] = "correct horse battery staple",
        }), _hasher);

        var user = Assert.Single(_db.Users);
        Assert.Equal("alice", user.Username);
        Assert.True(user.IsAdministrator);
        Assert.NotNull(user.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Success,
            _hasher.VerifyHashedPassword(user, user.PasswordHash!, "correct horse battery staple"));
    }

    [Fact]
    public async Task SeedAdminAsync_PasswordFileTakesPrecedenceOverPassword()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            await File.WriteAllTextAsync(tempFile, "from-the-file\n");
            await StartupSeeder.SeedAdminAsync(_db, Config(new()
            {
                ["Admin:Username"] = "alice",
                ["Admin:Password"] = "should-be-ignored",
                ["Admin:PasswordFile"] = tempFile,
            }), _hasher);

            var user = Assert.Single(_db.Users);
            Assert.Equal(
                PasswordVerificationResult.Success,
                _hasher.VerifyHashedPassword(user, user.PasswordHash!, "from-the-file"));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task SeedAdminAsync_UsernameAlreadyExists_DoesNotCreateOrModifyIt()
    {
        _db.Users.Add(new AppUser { Username = "alice", NormalizedUsername = "ALICE", IsAdministrator = false });
        await _db.SaveChangesAsync();

        await StartupSeeder.SeedAdminAsync(_db, Config(new()
        {
            ["Admin:Username"] = "alice",
            ["Admin:Password"] = "some-password",
        }), _hasher);

        var user = Assert.Single(_db.Users);
        Assert.False(user.IsAdministrator); // never silently promoted - create-only semantics
        Assert.Null(user.PasswordHash); // never silently given a password either
    }

    [Fact]
    public async Task SeedDefaultProviderAsync_ProvidersAlreadyExist_DoesNothing()
    {
        _db.AuthenticationProviders.Add(new AuthenticationProvider
        {
            Name = "existing", DisplayName = "Existing", Type = AuthProviderType.Oidc,
            ClientId = "c", ConfigurationJson = "{}",
        });
        await _db.SaveChangesAsync();

        await StartupSeeder.SeedDefaultProviderAsync(_db, Config(new()
        {
            ["OidcDefault:Authority"] = "https://new",
            ["OidcDefault:ClientId"] = "new-client",
            ["OidcDefault:ClientSecret"] = "new-secret",
        }), _secretProtector);

        var provider = Assert.Single(_db.AuthenticationProviders);
        Assert.Equal("Existing", provider.DisplayName);
    }

    [Fact]
    public async Task SeedDefaultProviderAsync_NothingConfigured_DoesNotCreateAProvider()
    {
        await StartupSeeder.SeedDefaultProviderAsync(_db, Config(new()), _secretProtector);

        Assert.Empty(_db.AuthenticationProviders);
    }

    [Fact]
    public async Task SeedDefaultProviderAsync_MissingClientSecret_DoesNotCreateAProvider()
    {
        // The old SPA-driven public-client flow never needed a secret; the new server-side
        // exchange always does, so an incomplete legacy-style config seeds nothing rather than
        // creating a provider that can never actually complete a sign-in.
        await StartupSeeder.SeedDefaultProviderAsync(_db, Config(new()
        {
            ["OidcDefault:Authority"] = "https://auth.example.com/realms/markdown-hub",
            ["OidcDefault:ClientId"] = "markdown-hub-spa",
        }), _secretProtector);

        Assert.Empty(_db.AuthenticationProviders);
    }

    [Fact]
    public async Task SeedDefaultProviderAsync_ConfiguredValues_CreatesEnabledProviderWithProtectedSecret()
    {
        await StartupSeeder.SeedDefaultProviderAsync(_db, Config(new()
        {
            ["OidcDefault:Name"] = "Keycloak",
            ["OidcDefault:Authority"] = "https://auth.example.com/realms/markdown-hub",
            ["OidcDefault:ClientId"] = "markdown-hub-spa",
            ["OidcDefault:ClientSecret"] = "s3cr3t",
            ["OidcDefault:Audience"] = "markdown-hub-api",
            ["OidcDefault:RequireHttpsMetadata"] = "true",
        }), _secretProtector);

        var provider = Assert.Single(_db.AuthenticationProviders);
        Assert.Equal("Keycloak", provider.DisplayName);
        Assert.True(provider.Enabled);
        Assert.NotNull(provider.ClientSecretProtected);
        Assert.NotEqual("s3cr3t", provider.ClientSecretProtected); // stored encrypted, not plaintext
        Assert.Equal("s3cr3t", _secretProtector.Unprotect(provider.ClientSecretProtected!));
    }
}
