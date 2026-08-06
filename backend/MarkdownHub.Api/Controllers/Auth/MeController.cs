using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.Auth;

public record SetDefaultFolderRequest(string? FolderPath);
public record ChangePasswordRequest(string? CurrentPassword, string NewPassword, string ConfirmNewPassword);
public record AuthMethodsResponse(bool HasPassword, IReadOnlyList<LinkedIdentityResponse> LinkedIdentities);
public record LinkedIdentityResponse(int Id, int ProviderId, string ProviderName, string ProviderDisplayName, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);
public record SessionResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset LastActivityAt, string? UserAgent, string? IpAddress, bool IsCurrent);

[ApiController]
[Route("api/me")]
[Authorize]
public class MeController : ControllerBase
{
    public const int MinPasswordLength = 8;
    public const int MaxPasswordLength = 256;

    private readonly CurrentUserService _currentUser;
    private readonly AppDbContext _db;
    private readonly AuditLogService _audit;
    private readonly IPasswordHasher<AppUser> _hasher;
    private readonly AccountSafetyService _safety;

    public MeController(CurrentUserService currentUser, AppDbContext db, AuditLogService audit,
        IPasswordHasher<AppUser> hasher, AccountSafetyService safety)
    {
        _currentUser = currentUser;
        _db = db;
        _audit = audit;
        _hasher = hasher;
        _safety = safety;
    }

    private Guid? CurrentSessionId => Guid.TryParse(User.FindFirstValue("sid"), out var id) ? id : null;

    /// <summary>Real login events (local or external) are audited where they actually happen -
    /// AuthController - so this profile fetch, called on every page load, doesn't itself
    /// double-log a login every time.</summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        return Ok(new { user.Id, user.Username, user.Email, user.DisplayName, user.IsAdministrator, user.DefaultFolderPath });
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        if (CurrentSessionId is { } sid)
        {
            var session = await _db.Sessions.FindAsync([sid], ct);
            if (session is not null) session.RevokedAt = DateTimeOffset.UtcNow;
        }
        await _audit.LogEventAsync(user.Id, "Auth.Logout", user.Username, "Auth", user.Id, ct: ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    /// <summary>Sets the folder the file tree should auto-expand to when this user opens the
    /// home page. A self-service preference - any authenticated user may set their own.</summary>
    [HttpPut("default-folder")]
    public async Task<IActionResult> SetDefaultFolder([FromBody] SetDefaultFolderRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        var trimmed = request.FolderPath?.Trim().Trim('/');
        user.DefaultFolderPath = string.IsNullOrEmpty(trimmed) ? null : trimmed;
        await _db.SaveChangesAsync(ct);
        return Ok(new { user.DefaultFolderPath });
    }

    /// <summary>Auth.md §7 - requires the current password unless the account has none yet
    /// (e.g. an external-provider-only account setting a password for the first time).
    /// Invalidates every other active session; the current one stays valid.</summary>
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        if (user.PasswordHash is not null)
        {
            if (string.IsNullOrEmpty(request.CurrentPassword) ||
                _hasher.VerifyHashedPassword(user, user.PasswordHash, request.CurrentPassword) == PasswordVerificationResult.Failed)
            {
                return BadRequest(new { message = "Current password is incorrect." });
            }
        }

        if (request.NewPassword != request.ConfirmNewPassword)
            return BadRequest(new { message = "New password and confirmation do not match." });
        if (request.NewPassword.Length < MinPasswordLength || request.NewPassword.Length > MaxPasswordLength)
            return BadRequest(new { message = $"Password must be between {MinPasswordLength} and {MaxPasswordLength} characters." });

        user.PasswordHash = _hasher.HashPassword(user, request.NewPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var otherSessions = await _db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAt == null && s.Id != CurrentSessionId)
            .ToListAsync(ct);
        foreach (var session in otherSessions) session.RevokedAt = DateTimeOffset.UtcNow;

        await _audit.LogEventAsync(user.Id, "Auth.PasswordChanged", user.Username, "Auth", user.Id, ct: ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("authentication-methods")]
    public async Task<IActionResult> GetAuthenticationMethods(CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        var identities = await _db.AuthenticationIdentities
            .Include(i => i.Provider)
            .Where(i => i.UserId == user.Id)
            .Select(i => new LinkedIdentityResponse(i.Id, i.AuthenticationProviderId, i.Provider!.Name, i.Provider!.DisplayName, i.CreatedAt, i.LastUsedAt))
            .ToListAsync(ct);

        return Ok(new AuthMethodsResponse(user.PasswordHash is not null, identities));
    }

    /// <summary>Removes one linked external identity. Refuses if it's this user's last usable
    /// authentication method (Auth.md §10/§31.6) - a local password can only be removed by never
    /// having been set, so there's no corresponding "remove password" endpoint.</summary>
    [HttpDelete("authentication-methods/{id:int}")]
    public async Task<IActionResult> RemoveAuthenticationMethod(int id, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        var identity = await _db.AuthenticationIdentities.Include(i => i.Provider)
            .FirstOrDefaultAsync(i => i.Id == id && i.UserId == user.Id, ct);
        if (identity is null) return NotFound();

        if (await _safety.WouldRemovalLeaveNoUsableMethodAsync(user.Id, ct))
            return BadRequest(new { message = "This is your only remaining sign-in method - link another one before removing it." });

        _db.AuthenticationIdentities.Remove(identity);
        await _audit.LogEventAsync(user.Id, "Auth.IdentityRemoved", user.Username, "Auth", user.Id, identity.Provider?.Name, ct: ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpGet("sessions")]
    public async Task<IActionResult> GetSessions(CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        // SQLite can't translate ORDER BY over a DateTimeOffset column - fetch then sort
        // client-side (same limitation/workaround already used elsewhere in this codebase, e.g.
        // AuditLogService).
        var sessions = await _db.Sessions
            .Where(s => s.UserId == user.Id && s.RevokedAt == null)
            .ToListAsync(ct);
        sessions = sessions.OrderByDescending(s => s.LastActivityAt).ToList();

        return Ok(sessions.Select(s => new SessionResponse(
            s.Id, s.CreatedAt, s.ExpiresAt, s.LastActivityAt, s.UserAgent, s.IpAddress, s.Id == CurrentSessionId)));
    }

    [HttpDelete("sessions/{id:guid}")]
    public async Task<IActionResult> RevokeSession(Guid id, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == id && s.UserId == user.Id, ct);
        if (session is null) return NotFound();

        session.RevokedAt = DateTimeOffset.UtcNow;
        await _audit.LogEventAsync(user.Id, "Auth.SessionRevoked", user.Username, "Auth", user.Id, ct: ct);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
