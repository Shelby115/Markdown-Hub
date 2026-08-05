using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Controllers;
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

    [Fact]
    public async Task List_OnlyReturnsEnabledProviders()
    {
        _db.OidcProviders.Add(new OidcProvider { Name = "Enabled", Authority = "https://a", ClientId = "a", Audience = "a", IsEnabled = true });
        _db.OidcProviders.Add(new OidcProvider { Name = "Disabled", Authority = "https://b", ClientId = "b", Audience = "b", IsEnabled = false });
        await _db.SaveChangesAsync();

        var result = Assert.IsType<OkObjectResult>(await _sut.List(CancellationToken.None));
        var providers = Assert.IsAssignableFrom<IEnumerable<AuthProviderResponse>>(result.Value);
        var single = Assert.Single(providers);
        Assert.Equal("Enabled", single.Name);
    }

    [Fact]
    public async Task List_DoesNotExposeAudience()
    {
        // AuthProviderResponse simply has no Audience property - this test documents that
        // omission is deliberate (resource-server-only detail the frontend never needs), so a
        // future edit adding it back would need to consciously change the DTO, not just the query.
        Assert.DoesNotContain("Audience", typeof(AuthProviderResponse).GetProperties().Select(p => p.Name));
    }
}
