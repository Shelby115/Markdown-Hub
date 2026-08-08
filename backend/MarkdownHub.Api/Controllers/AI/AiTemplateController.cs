using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.AI;

/// <summary>
/// AI Templates: a template page's {{Placeholder}} structure is parsed here and each placeholder
/// is generated individually, so the document's shape is enforced by the application and only the
/// content itself comes from the model. Nothing is written to the hub by this controller - the
/// frontend saves the assembled result through FilesController like any other new page.
/// The template is re-read and re-parsed on every call: a client can name a slot, never supply
/// the structure or the instructions.
/// </summary>
[ApiController]
[Authorize]
public class AiTemplateController : ControllerBase
{
    private const int MaxSlotValues = 100;
    private const int MaxSlotValueChars = 4000;

    private readonly AiTemplateService _aiTemplates;
    private readonly CurrentUserService _currentUser;
    private readonly PermissionService _permissions;
    private readonly MarkdownFileService _files;

    public AiTemplateController(AiTemplateService aiTemplates, CurrentUserService currentUser, PermissionService permissions, MarkdownFileService files)
    {
        _aiTemplates = aiTemplates;
        _currentUser = currentUser;
        _permissions = permissions;
        _files = files;
    }

    /// <summary>Parses a template into structure + slots. Deliberately makes no AI call, so the UI
    /// can open and explain itself even when the AI provider is unreachable. A template with no
    /// ai-template instruction block simply returns zero slots, which is how the frontend tells an
    /// AI Template apart from an ordinary one.</summary>
    [HttpPost("api/ai/template/parse")]
    public async Task<IActionResult> Parse([FromBody] AiTemplateParseRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        var (template, failure) = await LoadTemplateAsync(user.Id, request.TemplatePath, ct);
        if (failure is not null)
        {
            return failure;
        }

        var elements = template!.Elements
            .Select(e => new AiTemplateElementDto(e.LiteralText, e.Slot?.Id))
            .ToList();
        var slots = template.Slots
            .Select(s => new AiTemplateSlotDto(s.Id, s.Name, s.Index, s.Count))
            .ToList();
        return Ok(new AiTemplateParseResponse(elements, slots, template.FillInVariables));
    }

    /// <summary>Generates, rerolls, or improves exactly one slot. "Generate all" is the client
    /// calling this once per slot, so partial progress survives a failure partway through.</summary>
    [HttpPost("api/ai/template/generate")]
    public async Task<IActionResult> Generate([FromBody] AiTemplateGenerateRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null)
        {
            return Unauthorized();
        }

        if (!Enum.TryParse<AiTemplateMode>(request.Mode, ignoreCase: true, out var mode))
        {
            return BadRequest(new { message = $"Unknown generation mode '{request.Mode}'." });
        }

        var (template, failure) = await LoadTemplateAsync(user.Id, request.TemplatePath, ct);
        if (failure is not null)
        {
            return failure;
        }

        var slot = template!.Slots.FirstOrDefault(s => s.Id == request.SlotId);
        if (slot is null)
        {
            return BadRequest(new { message = $"This template has no placeholder '{request.SlotId}'." });
        }

        var slotValues = (request.Slots ?? [])
            .Take(MaxSlotValues)
            .Select(v => v with { Content = v.Content.Length > MaxSlotValueChars ? v.Content[..MaxSlotValueChars] : v.Content })
            .ToList();

        try
        {
            var result = await _aiTemplates.GenerateSlotAsync(template, slot, slotValues, mode, ct);
            return Ok(new AiTemplateGenerateResponse(result.Content, result.Warnings));
        }
        catch (AiServiceException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
    }

    /// <summary>Permission-check, read, and parse - the identical preamble for both endpoints.
    /// Returns either the parsed template or the error result to return to the caller.</summary>
    private async Task<(ParsedAiTemplate? Template, IActionResult? Failure)> LoadTemplateAsync(int userId, string templatePath, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(templatePath))
        {
            return (null, BadRequest(new { message = "A template path is required." }));
        }

        // Fail closed: a template the user can't view must not be readable through this endpoint,
        // even indirectly as generation instructions.
        if (!await _permissions.HasAtLeastAsync(userId, templatePath, PermissionLevel.View, ct))
        {
            return (null, Forbid());
        }

        try
        {
            var page = await _files.ReadAsync(templatePath, ct);
            return (AiTemplateParser.Parse(page.Content), null);
        }
        catch (FileNotFoundException)
        {
            return (null, NotFound(new { message = $"Template not found: {templatePath}" }));
        }
        catch (UnauthorizedAccessException)
        {
            return (null, Forbid());
        }
        catch (AiTemplateParseException ex)
        {
            return (null, BadRequest(new { message = ex.Message }));
        }
    }
}
