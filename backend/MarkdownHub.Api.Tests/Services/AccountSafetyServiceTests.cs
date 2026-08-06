using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Services;

public class AccountSafetyServiceTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AccountSafetyService _sut;

    public AccountSafetyServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _sut = new AccountSafetyService(_db);
    }

    public void Dispose() => _db.Dispose();

    private async Task<AuthenticationProvider> CreateProviderAsync(string name = "keycloak")
    {
        var provider = new AuthenticationProvider
        {
            Name = name, DisplayName = name, Type = AuthProviderType.Oidc, ClientId = "c", ConfigurationJson = "{}",
        };
        _db.AuthenticationProviders.Add(provider);
        await _db.SaveChangesAsync();
        return provider;
    }

    [Fact]
    public async Task WouldRemovalLeaveNoUsableMethod_UserWithOnlyOneMethod_ReturnsTrue()
    {
        var user = new AppUser { Username = "a", NormalizedUsername = "A", PasswordHash = "hash" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        Assert.True(await _sut.WouldRemovalLeaveNoUsableMethodAsync(user.Id));
    }

    [Fact]
    public async Task WouldRemovalLeaveNoUsableMethod_UserWithPasswordAndIdentity_ReturnsFalse()
    {
        var user = new AppUser { Username = "a", NormalizedUsername = "A", PasswordHash = "hash" };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        var provider = await CreateProviderAsync();
        _db.AuthenticationIdentities.Add(new AuthenticationIdentity { UserId = user.Id, AuthenticationProviderId = provider.Id, Subject = "sub" });
        await _db.SaveChangesAsync();

        Assert.False(await _sut.WouldRemovalLeaveNoUsableMethodAsync(user.Id));
    }

    [Fact]
    public async Task IsSoleAdministratorAsync_OnlyAdmin_ReturnsTrue()
    {
        var admin = new AppUser { Username = "admin", NormalizedUsername = "ADMIN", IsAdministrator = true };
        _db.Users.Add(admin);
        await _db.SaveChangesAsync();

        Assert.True(await _sut.IsSoleAdministratorAsync(admin.Id));
    }

    [Fact]
    public async Task IsSoleAdministratorAsync_AnotherActiveAdminExists_ReturnsFalse()
    {
        var admin1 = new AppUser { Username = "admin1", NormalizedUsername = "ADMIN1", IsAdministrator = true };
        var admin2 = new AppUser { Username = "admin2", NormalizedUsername = "ADMIN2", IsAdministrator = true };
        _db.Users.AddRange(admin1, admin2);
        await _db.SaveChangesAsync();

        Assert.False(await _sut.IsSoleAdministratorAsync(admin1.Id));
    }

    [Fact]
    public async Task IsSoleAdministratorAsync_OtherAdminIsDisabled_ReturnsTrue()
    {
        var admin1 = new AppUser { Username = "admin1", NormalizedUsername = "ADMIN1", IsAdministrator = true };
        var admin2 = new AppUser { Username = "admin2", NormalizedUsername = "ADMIN2", IsAdministrator = true, IsDisabled = true };
        _db.Users.AddRange(admin1, admin2);
        await _db.SaveChangesAsync();

        Assert.True(await _sut.IsSoleAdministratorAsync(admin1.Id));
    }

    [Fact]
    public async Task CountUsersUsingProviderAsync_CountsDistinctUsers()
    {
        var provider = await CreateProviderAsync();
        var user1 = new AppUser { Username = "u1", NormalizedUsername = "U1" };
        var user2 = new AppUser { Username = "u2", NormalizedUsername = "U2" };
        _db.Users.AddRange(user1, user2);
        await _db.SaveChangesAsync();
        _db.AuthenticationIdentities.Add(new AuthenticationIdentity { UserId = user1.Id, AuthenticationProviderId = provider.Id, Subject = "s1" });
        _db.AuthenticationIdentities.Add(new AuthenticationIdentity { UserId = user2.Id, AuthenticationProviderId = provider.Id, Subject = "s2" });
        await _db.SaveChangesAsync();

        Assert.Equal(2, await _sut.CountUsersUsingProviderAsync(provider.Id));
    }

    /// <summary>Auth.md §10's example, at the AccountSafetyService level: an admin with a local
    /// password AND Keycloak linked is unaffected by removing Keycloak.</summary>
    [Fact]
    public async Task WouldProviderRemovalStrandLastAdministrator_AdminHasPasswordToo_ReturnsFalse()
    {
        var provider = await CreateProviderAsync();
        var admin = new AppUser { Username = "admin", NormalizedUsername = "ADMIN", IsAdministrator = true, PasswordHash = "hash" };
        _db.Users.Add(admin);
        await _db.SaveChangesAsync();
        _db.AuthenticationIdentities.Add(new AuthenticationIdentity { UserId = admin.Id, AuthenticationProviderId = provider.Id, Subject = "sub" });
        await _db.SaveChangesAsync();

        Assert.False(await _sut.WouldProviderRemovalStrandLastAdministratorAsync(provider.Id));
    }

    [Fact]
    public async Task WouldProviderRemovalStrandLastAdministrator_ProviderIsSoleAdminsSoleMethod_ReturnsTrue()
    {
        var provider = await CreateProviderAsync();
        var admin = new AppUser { Username = "admin", NormalizedUsername = "ADMIN", IsAdministrator = true };
        _db.Users.Add(admin);
        await _db.SaveChangesAsync();
        _db.AuthenticationIdentities.Add(new AuthenticationIdentity { UserId = admin.Id, AuthenticationProviderId = provider.Id, Subject = "sub" });
        await _db.SaveChangesAsync();

        Assert.True(await _sut.WouldProviderRemovalStrandLastAdministratorAsync(provider.Id));
    }

    [Fact]
    public async Task WouldProviderRemovalStrandLastAdministrator_AdminDoesNotUseThisProvider_ReturnsFalse()
    {
        var usedProvider = await CreateProviderAsync("used");
        var otherProvider = await CreateProviderAsync("other");
        var admin = new AppUser { Username = "admin", NormalizedUsername = "ADMIN", IsAdministrator = true };
        _db.Users.Add(admin);
        await _db.SaveChangesAsync();
        _db.AuthenticationIdentities.Add(new AuthenticationIdentity { UserId = admin.Id, AuthenticationProviderId = usedProvider.Id, Subject = "sub" });
        await _db.SaveChangesAsync();

        Assert.False(await _sut.WouldProviderRemovalStrandLastAdministratorAsync(otherProvider.Id));
    }
}
