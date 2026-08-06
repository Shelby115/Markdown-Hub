using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.AI;

public enum AssistantAction
{
    Ask,
    Summarize,
    ExpandTopic,
}

public record AssistantRequest(string Action, string? Question, List<string> ContextPaths);
public record AssistantResultCard(string Title, string Content);
public record AssistantResponse(List<AssistantResultCard> Results);

/// <summary>
/// AI knowledge assistant: takes a set of hub pages the caller explicitly chose as context,
/// plus an action/question, and returns one or more result cards for the user to review. Never
/// modifies any page itself - "adding" a result to a page is a separate, ordinary save request
/// the frontend makes afterward (see FilesController), same as if the user had typed it in.
/// Every context page is permission-checked individually; the whole knowledge base is never
/// sent unless the caller explicitly selected every page.
/// </summary>
[ApiController]
[Route("api/ai/assistant")]
[Authorize]
public class AiAssistantController : ControllerBase
{
    private const int MaxContextPages = 20;
    private const int MaxContextCharsPerPage = 8000;

    private readonly IAiService _ai;
    private readonly CurrentUserService _currentUser;
    private readonly PermissionService _permissions;
    private readonly MarkdownFileService _files;

    public AiAssistantController(IAiService ai, CurrentUserService currentUser, PermissionService permissions, MarkdownFileService files)
    {
        _ai = ai;
        _currentUser = currentUser;
        _permissions = permissions;
        _files = files;
    }

    /// <summary>Lets the panel show an upfront "not configured" state instead of only failing
    /// once someone tries to use it. Reuses ListModelsAsync rather than a dedicated ping - an
    /// unreachable provider throws, and a reachable one with nothing installed returns an empty
    /// list; either case means there's nothing usable yet.</summary>
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        try
        {
            var models = await _ai.ListModelsAsync(ct);
            return Ok(new { available = models.Count > 0 });
        }
        catch (AiServiceException)
        {
            return Ok(new { available = false });
        }
    }

    [HttpPost]
    public async Task<IActionResult> Ask([FromBody] AssistantRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        if (!Enum.TryParse<AssistantAction>(request.Action, ignoreCase: true, out var action))
            return BadRequest(new { message = $"Unknown assistant action '{request.Action}'." });
        if (action == AssistantAction.Ask && string.IsNullOrWhiteSpace(request.Question))
            return BadRequest(new { message = "A question is required for the Ask action." });

        var contextPaths = (request.ContextPaths ?? []).Distinct().Take(MaxContextPages).ToList();
        if (contextPaths.Count == 0)
            return BadRequest(new { message = "Select at least one page as context." });

        var contextBlocks = new List<string>();
        foreach (var path in contextPaths)
        {
            // Fail closed: a user must never be able to feed the AI a page they can't
            // themselves view, even indirectly via this endpoint.
            if (!await _permissions.HasAtLeastAsync(user.Id, path, PermissionLevel.View, ct))
                return Forbid();

            try
            {
                var page = await _files.ReadAsync(path, ct);
                var content = page.Content.Length > MaxContextCharsPerPage
                    ? page.Content[..MaxContextCharsPerPage] + "\n…[truncated]"
                    : page.Content;
                contextBlocks.Add($"PAGE: {page.PageName}\nPATH: {path}\n\n{content}");
            }
            catch (FileNotFoundException)
            {
                return NotFound(new { message = $"Page not found: {path}" });
            }
        }

        var contextText = "KNOWLEDGE CONTEXT\n\n" + string.Join("\n\n---\n\n", contextBlocks);
        var userPrompt = action switch
        {
            AssistantAction.Summarize => $"{contextText}\n\nTask: Summarize the knowledge above.",
            AssistantAction.ExpandTopic => $"{contextText}\n\nTask: Propose additional information, in Markdown, that would make this " +
                $"knowledge more complete.{(string.IsNullOrWhiteSpace(request.Question) ? "" : $" Focus on: {request.Question}")}",
            AssistantAction.Ask => $"{contextText}\n\nQuestion: {request.Question}",
            _ => throw new InvalidOperationException($"Unhandled {nameof(AssistantAction)}: {action}"),
        };

        try
        {
            var result = await _ai.CompleteAsync(AiPrompts.AssistantSystemPrompt, userPrompt, ct);
            var title = action switch
            {
                AssistantAction.Summarize => "Summary",
                AssistantAction.ExpandTopic => "Suggested addition",
                AssistantAction.Ask => "Answer",
                _ => "Result",
            };
            return Ok(new AssistantResponse([new AssistantResultCard(title, result)]));
        }
        catch (AiServiceException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
    }
}
