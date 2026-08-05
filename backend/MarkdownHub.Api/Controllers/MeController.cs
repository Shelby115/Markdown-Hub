using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

public record SetDefaultFolderRequest(string? FolderPath);

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    private readonly CurrentUserService _currentUser;
    private readonly AppDbContext _db;
    private readonly AuditLogService _audit;

    public MeController(CurrentUserService currentUser, AppDbContext db, AuditLogService audit)
    {
        _currentUser = currentUser;
        _db = db;
        _audit = audit;
    }

    /// <summary>
    /// Called once by the SPA right after it obtains a valid Keycloak session (see App.tsx's
    /// post-auth effect) - the closest thing this stateless-JWT architecture has to a "login"
    /// boundary, since Keycloak's own login page is entirely outside this API's view. Logged
    /// here rather than on every authenticated request (GetOrCreateAsync runs on all of those).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await _currentUser.GetOrCreateAsync(ct);
        if (user is null) return Unauthorized();
        await _audit.LogEventAsync(user.Id, "Auth.Login", user.Username, "Auth", user.Id, ct: ct);
        return Ok(new { user.Id, user.Username, user.Email, user.IsAdministrator, user.DefaultFolderPath });
    }

    /// <summary>Called by the SPA immediately before it redirects to Keycloak's own logout
    /// endpoint - records the event on this side since Keycloak's logout page is, again,
    /// entirely outside this API's view.</summary>
    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var user = await _currentUser.GetOrCreateAsync(ct);
        if (user is null) return Unauthorized();
        await _audit.LogEventAsync(user.Id, "Auth.Logout", user.Username, "Auth", user.Id, ct: ct);
        return NoContent();
    }

    /// <summary>Sets the folder the file tree should auto-expand to when this user opens the
    /// home page. A self-service preference - any authenticated user may set their own.</summary>
    [HttpPut("default-folder")]
    public async Task<IActionResult> SetDefaultFolder([FromBody] SetDefaultFolderRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetOrCreateAsync(ct);
        if (user is null) return Unauthorized();

        var trimmed = request.FolderPath?.Trim().Trim('/');
        user.DefaultFolderPath = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        await _db.SaveChangesAsync(ct);
        return Ok(new { user.DefaultFolderPath });
    }
}
