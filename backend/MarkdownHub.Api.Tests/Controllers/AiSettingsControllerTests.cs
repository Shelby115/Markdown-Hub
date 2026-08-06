using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Controllers;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

public class AiSettingsControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeAiService _ai;
    private readonly AiSettingsController _sut;
    private readonly AppUser _admin;

    public AiSettingsControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _admin = new AppUser { Username = "admin", NormalizedUsername = "ADMIN", IsAdministrator = true };
        _db.Users.Add(_admin);
        _db.SaveChanges();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ai:Ollama:Model"] = "gpt-oss:20b" })
            .Build();

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
        _ai = new FakeAiService();
        _sut = new AiSettingsController(_db, _ai, config, currentUser, new AuditLogService(_db, httpContextAccessor));
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GetSettings_NoOverride_ReturnsConfiguredDefaultAsEffective()
    {
        var result = await _sut.GetSettings(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AiSettingsResponse>(ok.Value);
        Assert.Null(response.SelectedModel);
        Assert.Equal("gpt-oss:20b", response.ConfiguredDefaultModel);
        Assert.Equal("gpt-oss:20b", response.EffectiveModel);
    }

    [Fact]
    public async Task SetModel_PersistsOverrideAndReturnsItAsEffective()
    {
        await _sut.SetModel(new SetAiModelRequest("llama3.1:8b"), CancellationToken.None);

        var result = await _sut.GetSettings(CancellationToken.None);
        var response = Assert.IsType<AiSettingsResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Equal("llama3.1:8b", response.SelectedModel);
        Assert.Equal("llama3.1:8b", response.EffectiveModel);
    }

    [Fact]
    public async Task SetModel_ThenClearingIt_RevertsToConfiguredDefault()
    {
        await _sut.SetModel(new SetAiModelRequest("llama3.1:8b"), CancellationToken.None);
        await _sut.SetModel(new SetAiModelRequest(null), CancellationToken.None);

        var result = await _sut.GetSettings(CancellationToken.None);
        var response = Assert.IsType<AiSettingsResponse>(Assert.IsType<OkObjectResult>(result).Value);
        Assert.Null(response.SelectedModel);
        Assert.Equal("gpt-oss:20b", response.EffectiveModel);
    }

    [Fact]
    public async Task SetModel_RecordsAuditEntry()
    {
        await _sut.SetModel(new SetAiModelRequest("llama3.1:8b"), CancellationToken.None);

        var entry = Assert.Single(_db.AuditLog);
        Assert.Equal("AiSettings.SetModel", entry.Action);
        Assert.Equal("llama3.1:8b", entry.Details);
        Assert.Equal(_admin.Id, entry.AppUserId);
    }

    [Fact]
    public async Task ListModels_ReturnsModelsFromTheAiService()
    {
        _ai.Models = ["gpt-oss:20b", "llama3.1:8b"];

        var result = await _sut.ListModels(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var models = Assert.IsAssignableFrom<IReadOnlyList<string>>(
            ok.Value!.GetType().GetProperty("models")!.GetValue(ok.Value));
        Assert.Equal(["gpt-oss:20b", "llama3.1:8b"], models);
    }

    [Fact]
    public async Task ListModels_AiServiceThrows_ReturnsBadGateway()
    {
        _ai.ThrowOnListModels = new AiServiceException("Couldn't reach the AI service.");

        var result = await _sut.ListModels(CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }
}
