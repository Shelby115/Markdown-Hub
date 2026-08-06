using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using MarkdownHub.Api.Controllers.AI;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

public class AiAssistantControllerTests : IDisposable
{
    private readonly string _hubRoot;
    private readonly AppDbContext _db;
    private readonly FakeAiService _ai;
    private readonly AiAssistantController _sut;
    private readonly AppUser _user;

    public AiAssistantControllerTests()
    {
        _hubRoot = Directory.CreateTempSubdirectory("markdown-hub-tests-hub-").FullName;
        Directory.CreateDirectory(Path.Combine(_hubRoot, "Public"));
        File.WriteAllText(Path.Combine(_hubRoot, "Public", "Gandalf.md"), "Gandalf is a wizard.");
        Directory.CreateDirectory(Path.Combine(_hubRoot, "Private"));
        File.WriteAllText(Path.Combine(_hubRoot, "Private", "Secret.md"), "top secret plans");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Hub:MarkdownRoot"] = _hubRoot })
            .Build();
        var hub = new HubPathService(config);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        _user = new AppUser { Username = "gm", NormalizedUsername = "GM" };
        _db.Users.Add(_user);
        // Only the "Public" folder is granted - "Private/Secret.md" deliberately is not.
        _db.FolderPermissions.Add(new FolderPermission { AppUserId = 0, FolderPath = "Public", Level = PermissionLevel.View });
        _db.SaveChanges();
        var grant = _db.FolderPermissions.First();
        grant.AppUserId = _user.Id;
        _db.SaveChanges();

        var permissions = new PermissionService(_db, hub);
        var search = new SearchIndexService(config);
        var versions = new VersionService(_db);
        var files = new MarkdownFileService(hub, _db, search, versions);

        var httpContextAccessor = new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, _user.Id.ToString()),
                    new Claim("preferred_username", _user.Username),
                ]))
            }
        };
        var currentUser = new CurrentUserService(_db, httpContextAccessor);
        _ai = new FakeAiService();
        _sut = new AiAssistantController(_ai, currentUser, permissions, files);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_hubRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Ask_NoContextPaths_ReturnsBadRequest()
    {
        var result = await _sut.Ask(new AssistantRequest("Summarize", null, []), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Ask_ActionWithoutQuestion_ReturnsBadRequest()
    {
        var result = await _sut.Ask(new AssistantRequest("Ask", null, ["Public/Gandalf.md"]), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Ask_UnknownAction_ReturnsBadRequest()
    {
        var result = await _sut.Ask(new AssistantRequest("Research", "q", ["Public/Gandalf.md"]), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Ask_ContextPageWithoutPermission_ReturnsForbid()
    {
        var result = await _sut.Ask(new AssistantRequest("Summarize", null, ["Private/Secret.md"]), CancellationToken.None);
        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Ask_ContextPageWithoutPermission_NeverReachesTheAiService()
    {
        await _sut.Ask(new AssistantRequest("Summarize", null, ["Private/Secret.md"]), CancellationToken.None);
        Assert.Null(_ai.LastUserPrompt); // the AI was never called with the unauthorized content
    }

    [Fact]
    public async Task Ask_Summarize_SendsAssistantSystemPromptAndPageContentAsContext()
    {
        _ai.Respond = (_, _) => "A wizard.";

        var result = await _sut.Ask(new AssistantRequest("Summarize", null, ["Public/Gandalf.md"]), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AssistantResponse>(ok.Value);
        Assert.Single(response.Results);
        Assert.Equal("A wizard.", response.Results[0].Content);
        Assert.Equal(AiPrompts.AssistantSystemPrompt, _ai.LastSystemPrompt);
        Assert.Contains("Gandalf is a wizard.", _ai.LastUserPrompt);
        Assert.Contains("PATH: Public/Gandalf.md", _ai.LastUserPrompt);
    }

    [Fact]
    public async Task Ask_Ask_IncludesTheQuestionInThePrompt()
    {
        await _sut.Ask(new AssistantRequest("Ask", "What is Gandalf?", ["Public/Gandalf.md"]), CancellationToken.None);
        Assert.Contains("What is Gandalf?", _ai.LastUserPrompt);
    }

    [Fact]
    public async Task Ask_AiServiceThrows_ReturnsBadGateway()
    {
        _ai.ThrowOnComplete = new AiServiceException("Ollama is unreachable.");

        var result = await _sut.Ask(new AssistantRequest("Summarize", null, ["Public/Gandalf.md"]), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }

    [Fact]
    public async Task GetStatus_OllamaUnreachable_ReturnsUnavailable()
    {
        _ai.ThrowOnListModels = new AiServiceException("Couldn't reach the AI service.");

        var result = await _sut.GetStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False((bool)ok.Value!.GetType().GetProperty("available")!.GetValue(ok.Value)!);
    }

    [Fact]
    public async Task GetStatus_ReachableButNoModelsInstalled_ReturnsUnavailable()
    {
        _ai.Models = [];

        var result = await _sut.GetStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.False((bool)ok.Value!.GetType().GetProperty("available")!.GetValue(ok.Value)!);
    }

    [Fact]
    public async Task GetStatus_ReachableWithModelsInstalled_ReturnsAvailable()
    {
        _ai.Models = ["gpt-oss:20b"];

        var result = await _sut.GetStatus(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.True((bool)ok.Value!.GetType().GetProperty("available")!.GetValue(ok.Value)!);
    }
}
