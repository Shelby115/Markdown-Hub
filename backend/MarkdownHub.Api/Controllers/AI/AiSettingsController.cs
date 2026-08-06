using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.AI;

public record AiSettingsResponse(string? SelectedModel, string ConfiguredDefaultModel, string EffectiveModel);
public record SetAiModelRequest(string? Model);

/// <summary>
/// Admin-only control over which Ollama model the whole app uses. This is a single, shared,
/// app-wide setting (not per-user) - everyone's AI-assisted editing and knowledge assistant
/// requests use whatever model is selected here. Stored in AppSettings, read fresh by
/// OllamaAiService on every request, so a change here takes effect immediately - no restart.
/// </summary>
[ApiController]
[Route("api/admin/ai")]
[Authorize(Policy = "RequireAdministrator")]
public class AiSettingsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IAiService _ai;
    private readonly IConfiguration _config;
    private readonly CurrentUserService _currentUser;
    private readonly AuditLogService _audit;

    public AiSettingsController(AppDbContext db, IAiService ai, IConfiguration config, CurrentUserService currentUser, AuditLogService audit)
    {
        _db = db;
        _ai = ai;
        _config = config;
        _currentUser = currentUser;
        _audit = audit;
    }

    [HttpGet("models")]
    public async Task<IActionResult> ListModels(CancellationToken ct)
    {
        try
        {
            var models = await _ai.ListModelsAsync(ct);
            return Ok(new { models });
        }
        catch (AiServiceException ex)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { message = ex.Message });
        }
    }

    [HttpGet("settings")]
    public async Task<IActionResult> GetSettings(CancellationToken ct)
    {
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == AppSetting.AiOllamaModelKey, ct);
        var configuredDefault = _config["Ai:Ollama:Model"] ?? "gpt-oss:20b";
        var selected = string.IsNullOrWhiteSpace(setting?.Value) ? null : setting.Value;
        return Ok(new AiSettingsResponse(selected, configuredDefault, selected ?? configuredDefault));
    }

    /// <summary>Sets the model override, or clears it (reverting to the configured default) when
    /// Model is null/blank.</summary>
    [HttpPut("settings")]
    public async Task<IActionResult> SetModel([FromBody] SetAiModelRequest request, CancellationToken ct)
    {
        var trimmed = request.Model?.Trim();
        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == AppSetting.AiOllamaModelKey, ct);

        if (string.IsNullOrEmpty(trimmed))
        {
            if (setting is not null) _db.Settings.Remove(setting);
        }
        else if (setting is null)
        {
            _db.Settings.Add(new AppSetting { Key = AppSetting.AiOllamaModelKey, Value = trimmed });
        }
        else
        {
            setting.Value = trimmed;
        }
        await _db.SaveChangesAsync(ct);

        var actingUser = await _currentUser.GetCurrentAsync(ct);
        await _audit.LogEventAsync(actingUser?.Id, "AiSettings.SetModel", AppSetting.AiOllamaModelKey, "Setting", null,
            trimmed ?? "(cleared - reverted to default)", ct: ct);

        return await GetSettings(ct);
    }
}
