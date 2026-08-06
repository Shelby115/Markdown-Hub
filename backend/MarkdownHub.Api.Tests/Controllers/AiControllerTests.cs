using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Controllers;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Tests.Controllers;

public class FakeAiService : IAiService
{
    public string? LastSystemPrompt;
    public string? LastUserPrompt;
    public Func<string, string, string>? Respond;
    public Exception? ThrowOnComplete;
    public Exception? ThrowOnListModels;
    public IReadOnlyList<string> Models { get; set; } = [];

    public Task<string> CompleteAsync(string systemPrompt, string userPrompt, CancellationToken ct = default)
    {
        LastSystemPrompt = systemPrompt;
        LastUserPrompt = userPrompt;
        if (ThrowOnComplete is not null) throw ThrowOnComplete;
        return Task.FromResult(Respond?.Invoke(systemPrompt, userPrompt) ?? "AI reply");
    }

    public Task<IReadOnlyList<string>> ListModelsAsync(CancellationToken ct = default)
    {
        if (ThrowOnListModels is not null) throw ThrowOnListModels;
        return Task.FromResult(Models);
    }
}

public class AiControllerTests : IDisposable
{
    private readonly AppDbContext _db;
    private readonly FakeAiService _ai;
    private readonly AiController _sut;

    public AiControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        _db = new AppDbContext(options);

        var user = new AppUser { Username = "writer", NormalizedUsername = "WRITER" };
        _db.Users.Add(user);
        _db.SaveChanges();

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
        _sut = new AiController(_ai, currentUser);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Edit_ValidSummarizeRequest_ReturnsAiResult()
    {
        _ai.Respond = (_, userText) => $"Summary of: {userText}";

        var result = await _sut.Edit(new AiEditRequest("Summarize", "long text here"), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var response = Assert.IsType<AiEditResponse>(ok.Value);
        Assert.Equal("Summary of: long text here", response.Result);
        Assert.Equal(AiPrompts.SystemPromptFor(AiEditAction.Summarize), _ai.LastSystemPrompt);
    }

    [Theory]
    [InlineData("Summarize")]
    [InlineData("summarize")]
    [InlineData("ImproveWriting")]
    [InlineData("FixGrammar")]
    public async Task Edit_AcceptsAllKnownActionsCaseInsensitively(string action)
    {
        var result = await _sut.Edit(new AiEditRequest(action, "some text"), CancellationToken.None);
        Assert.IsType<OkObjectResult>(result);
    }

    [Fact]
    public async Task Edit_UnknownAction_ReturnsBadRequest()
    {
        var result = await _sut.Edit(new AiEditRequest("Rewrite", "text"), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Edit_EmptyText_ReturnsBadRequest()
    {
        var result = await _sut.Edit(new AiEditRequest("Summarize", "   "), CancellationToken.None);
        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task Edit_TextTooLong_ReturnsBadRequestWithoutCallingAiService()
    {
        var result = await _sut.Edit(new AiEditRequest("Summarize", new string('a', 20_001)), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(_ai.LastUserPrompt); // never reached the AI service
    }

    [Fact]
    public async Task Edit_AiServiceThrows_ReturnsBadGatewayWithMessage()
    {
        _ai.ThrowOnComplete = new AiServiceException("Ollama is unreachable.");

        var result = await _sut.Edit(new AiEditRequest("Summarize", "text"), CancellationToken.None);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status502BadGateway, status.StatusCode);
    }
}
