using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.AI;

/// <summary>
/// Text-in/text-out AI editing actions (summarize, improve writing, fix grammar) operating on
/// arbitrary submitted text - not tied to a specific hub file, so there's no folder permission
/// to check here beyond being an authenticated, non-disabled user. Never talks to Ollama
/// directly; always through IAiService, per that abstraction's whole purpose.
/// </summary>
[ApiController]
[Authorize]
public class AiController : ControllerBase
{
    private const int MaxInputLength = 20_000; // characters - keeps requests to a reasonable size/cost

    private readonly IAiService _ai;
    private readonly CurrentUserService _currentUser;

    public AiController(IAiService ai, CurrentUserService currentUser)
    {
        _ai = ai;
        _currentUser = currentUser;
    }

    [HttpPost("api/ai/edit")]
    public async Task<IActionResult> Edit([FromBody] AiEditRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!Enum.TryParse<AiEditAction>(request.Action, ignoreCase: true, out var action))
        {
            return BadRequest(new { message = $"Unknown AI action '{request.Action}'." });
        }

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            return BadRequest(new { message = "Text is required." });
        }

        if (request.Text.Length > MaxInputLength)
        {
            return BadRequest(new { message = $"Text is too long for AI editing (max {MaxInputLength} characters)." });
        }

        try
        {
            var result = await _ai.CompleteAsync(AiPrompts.SystemPromptFor(action), request.Text, ct);
            return Ok(new AiEditResponse(result));
        }
        catch (AiServiceException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
    }
}
