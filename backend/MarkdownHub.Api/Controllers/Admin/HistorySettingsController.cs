using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.Admin;

public record SetHistorySettingsRequest(int VersionRetentionDays, int ActivityRetentionDays, int ActivityDefaultDays);

/// <summary>Admin-only retention configuration for Version History and the Activity Log
/// (Activity-And-History.md section 6). Backed by AppSetting, same as the AI model override.</summary>
[ApiController]
[Authorize(Policy = "RequireAdministrator")]
public class HistorySettingsController : ControllerBase
{
    private readonly HistorySettingsService _settings;
    private readonly CurrentUserService _currentUser;
    private readonly AuditLogService _audit;

    public HistorySettingsController(HistorySettingsService settings, CurrentUserService currentUser, AuditLogService audit)
    {
        _settings = settings;
        _currentUser = currentUser;
        _audit = audit;
    }

    [HttpGet("api/admin/history-settings")]
    public async Task<IActionResult> Get(CancellationToken ct) => Ok(await _settings.GetAllAsync(ct));

    [HttpPut("api/admin/history-settings")]
    public async Task<IActionResult> Set([FromBody] SetHistorySettingsRequest request, CancellationToken ct)
    {
        try
        {
            HistorySettingsService.Validate(request.VersionRetentionDays);
            HistorySettingsService.Validate(request.ActivityRetentionDays);
            HistorySettingsService.Validate(request.ActivityDefaultDays);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return BadRequest(new { message = ex.Message });
        }

        if (request.ActivityDefaultDays > request.ActivityRetentionDays)
            return BadRequest(new { message = "The default activity view window can't exceed the activity retention period." });

        var actingUser = await _currentUser.GetCurrentAsync(ct);
        await _settings.SetAsync(HistorySettingsService.VersionRetentionDaysKey, request.VersionRetentionDays, ct);
        await _settings.SetAsync(HistorySettingsService.ActivityRetentionDaysKey, request.ActivityRetentionDays, ct);
        await _settings.SetAsync(HistorySettingsService.ActivityDefaultDaysKey, request.ActivityDefaultDays, ct);

        await _audit.LogEventAsync(actingUser?.Id, "Settings.HistoryRetention",
            $"versions={request.VersionRetentionDays}d, activity={request.ActivityRetentionDays}d, activityDefault={request.ActivityDefaultDays}d",
            "Setting", null, ct: ct);

        return Ok(await _settings.GetAllAsync(ct));
    }
}
