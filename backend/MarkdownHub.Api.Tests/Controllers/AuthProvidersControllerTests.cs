using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Controllers.Auth;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Tests.Controllers;

public class AuthProvidersControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly AuthProvidersController _sut;

    public AuthProvidersControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);
        _sut = new AuthProvidersController(_db);
    }

    public void Dispose() => _db.Dispose();

    private static AuthenticationProvider MakeProvider(string name, bool enabled) => new()
    {
        Name = name.ToLowerInvariant(),
        DisplayName = name,
        Type = AuthProviderType.Oidc,
        ClientId = "client",
        ConfigurationJson = "{}",
        Enabled = enabled,
    };

    [Fact]
    public async Task List_OnlyReturnsEnabledProviders()
    {
        _db.AuthenticationProviders.Add(MakeProvider("Enabled", enabled: true));
        _db.AuthenticationProviders.Add(MakeProvider("Disabled", enabled: false));
        await _db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await _sut.List(CancellationToken.None));
        var providers = Assert.IsAssignableFrom<IEnumerable<AuthProviderResponse>>(result.Value);
        var single = Assert.Single(providers);
        Assert.Equal("Enabled", single.DisplayName);
    }

    [Fact]
    public async Task List_WithNoProvidersConfigured_ReturnsEmptyList()
    {
        // Local username/password works with zero external providers - this must return an
        // empty list, not an error (Auth.md §5/§9).
        var result = Assert.IsType<OkObjectResult>(await _sut.List(CancellationToken.None));
        var providers = Assert.IsAssignableFrom<IEnumerable<AuthProviderResponse>>(result.Value);
        Assert.Empty(providers);
    }

    [Fact]
    public void Response_DoesNotExposeClientSecretOrConfiguration()
    {
        // AuthProviderResponse simply has no such properties - this test documents that
        // omission is deliberate (server-only detail the frontend never needs before login), so
        // a future edit adding one back would need to consciously change the DTO, not just the
        // query.
        var propertyNames = typeof(AuthProviderResponse).GetProperties().Select(p => p.Name).ToList();
        Assert.DoesNotContain("ClientSecret", propertyNames);
        Assert.DoesNotContain("Configuration", propertyNames);
    }
}
