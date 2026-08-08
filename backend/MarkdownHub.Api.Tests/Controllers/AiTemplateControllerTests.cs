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

public class AiTemplateControllerTests : IDisposable
{
    private const string AdventureTemplate = """
        # Adventure

        {{Scene}}

        {{Interactible}}
        {{Interactible}}

        ```ai-template
        Scene:
        - Random biome and location.

        Interactible:
        - One brief interactible.
        ```
        """;

    private readonly string _hubRoot;
    private readonly AppDbContext _db;
    private readonly FakeAiService _ai;
    private readonly AiTemplateController _sut;

    public AiTemplateControllerTests()
    {
        _hubRoot = Directory.CreateTempSubdirectory("markdown-hub-tests-hub-").FullName;
        Directory.CreateDirectory(Path.Combine(_hubRoot, "Public"));
        File.WriteAllText(Path.Combine(_hubRoot, "Public", "Adventure.md"), AdventureTemplate);
        File.WriteAllText(Path.Combine(_hubRoot, "Public", "Plain.md"), "# Notes\n\n{{Author}}\n");
        Directory.CreateDirectory(Path.Combine(_hubRoot, "Private"));
        File.WriteAllText(Path.Combine(_hubRoot, "Private", "Secret.md"), "{{Scene}}\n\n```ai-template\nScene:\n- secret\n```");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Hub:MarkdownRoot"] = _hubRoot })
            .Build();
        var hub = new HubPathService(config);

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var user = new AppUser { Username = "gm", NormalizedUsername = "GM" };
        _db.Users.Add(user);
        _db.FolderPermissions.Add(new FolderPermission { AppUserId = 0, FolderPath = "Public", Level = PermissionLevel.View });
        _db.SaveChanges();
        var grant = _db.FolderPermissions.First();
        grant.AppUserId = user.Id;
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
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim("preferred_username", user.Username),
                ]))
            }
        };
        var currentUser = new CurrentUserService(_db, httpContextAccessor);
        _ai = new FakeAiService();
        _sut = new AiTemplateController(new AiTemplateService(_ai), currentUser, permissions, files);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_hubRoot, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task Parse_ReturnsSlotsAndStructure()
    {
        var result = await _sut.Parse(new AiTemplateParseRequest("Public/Adventure.md"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AiTemplateParseResponse>(ok.Value);
        Assert.Equal(["Scene#1", "Interactible#1", "Interactible#2"], response.Slots.Select(s => s.Id));
        Assert.Contains(response.Elements, e => e.Text?.Contains("# Adventure") == true);
        Assert.DoesNotContain(response.Elements, e => e.Text?.Contains("ai-template") == true);
    }

    [Fact]
    public async Task Parse_OrdinaryTemplate_ReturnsNoSlots()
    {
        var result = await _sut.Parse(new AiTemplateParseRequest("Public/Plain.md"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AiTemplateParseResponse>(ok.Value);
        Assert.Empty(response.Slots);
        Assert.Equal(["Author"], response.FillInVariables);
    }

    [Fact]
    public async Task Parse_TemplateWithoutViewPermission_ReturnsForbid()
    {
        var result = await _sut.Parse(new AiTemplateParseRequest("Private/Secret.md"), CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
    }

    [Fact]
    public async Task Parse_MissingTemplate_ReturnsNotFound()
    {
        var result = await _sut.Parse(new AiTemplateParseRequest("Public/Nope.md"), CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task Generate_FillsTheNamedSlot()
    {
        _ai.Respond = (_, _) => "An abandoned mine in an orange hill.";

        var result = await _sut.Generate(
            new AiTemplateGenerateRequest("Public/Adventure.md", "Scene#1", "Generate", []),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AiTemplateGenerateResponse>(ok.Value);
        Assert.Equal("An abandoned mine in an orange hill.", response.Content);
        Assert.Empty(response.Warnings);
    }

    [Fact]
    public async Task Generate_UnknownSlot_ReturnsBadRequest()
    {
        var result = await _sut.Generate(
            new AiTemplateGenerateRequest("Public/Adventure.md", "Scene#7", "Generate", []),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Generate_UnknownMode_ReturnsBadRequest()
    {
        var result = await _sut.Generate(
            new AiTemplateGenerateRequest("Public/Adventure.md", "Scene#1", "Embellish", []),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Generate_TemplateWithoutViewPermission_NeverReachesTheAiService()
    {
        var result = await _sut.Generate(
            new AiTemplateGenerateRequest("Private/Secret.md", "Scene#1", "Generate", []),
            CancellationToken.None);

        Assert.IsType<ForbidResult>(result);
        Assert.Null(_ai.LastUserPrompt);
    }

    [Fact]
    public async Task Generate_AiServiceThrows_ReturnsBadGateway()
    {
        _ai.ThrowOnComplete = new AiServiceException("Ollama is unreachable.");

        var result = await _sut.Generate(
            new AiTemplateGenerateRequest("Public/Adventure.md", "Scene#1", "Generate", []),
            CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }
}
