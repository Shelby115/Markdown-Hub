using System.Security.Cryptography;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.Auth;

public record GrantPermissionRequest(int AppUserId, string FolderPath, PermissionLevel Level);
public record CreateUserRequest(string Username, string? TemporaryPassword, bool IsAdministrator = false);
public record CreateUserResponse(int Id, string Username, bool IsAdministrator, string TemporaryPassword);
public record AdminSetPasswordRequest(string NewPassword);

/// <summary>
/// Administrator-only user and permission management. Authorization here is enforced via the
/// "RequireAdministrator" policy (see Program.cs), which checks the local AppUser.IsAdministrator
/// flag - external provider claims are NEVER trusted for this (Auth.md §23).
///
/// User pre-provisioning (Auth.md §29 migration note): an admin creates a local account with a
/// temporary password here and hands it to the person out-of-band; they log in locally once, then
/// self-link Google/Keycloak/etc. from their own Account page (Auth.md §14/§16 - linking must
/// always go through an already-authenticated session, never an automatic username/email match).
/// </summary>
[ApiController]
[Authorize(Policy = "RequireAdministrator")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly AuditLogService _audit;
    private readonly IPasswordHasher<AppUser> _hasher;
    private readonly AccountSafetyService _safety;

    public UsersController(AppDbContext db, CurrentUserService currentUser, AuditLogService audit,
        IPasswordHasher<AppUser> hasher, AccountSafetyService safety)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _hasher = hasher;
        _safety = safety;
    }

    [HttpGet("api/admin/users")]
    public async Task<IActionResult> ListUsers(CancellationToken ct)
    {
        var users = await _db.Users
            .Select(u => new
            {
                u.Id,
                u.Username,
                u.Email,
                u.IsAdministrator,
                u.IsDisabled,
                u.CreatedAt,
                u.LastLoginAt,
                HasPassword = u.PasswordHash != null,
                LinkedIdentityCount = u.AuthenticationIdentities.Count,
            })
            .ToListAsync(ct);
        return Ok(users);
    }

    [HttpPost("api/admin/users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        if (string.IsNullOrEmpty(username)) return BadRequest("Username is required.");

        var normalized = AppUser.Normalize(username);
        var exists = await _db.Users.AnyAsync(u => u.NormalizedUsername == normalized, ct);
        if (exists) return Conflict("A user with that username already exists.");

        var temporaryPassword = string.IsNullOrEmpty(request.TemporaryPassword)
            ? GenerateTemporaryPassword()
            : request.TemporaryPassword;
        if (temporaryPassword.Length < AccountController.MinPasswordLength)
            return BadRequest($"Temporary password must be at least {AccountController.MinPasswordLength} characters.");

        var user = new AppUser
        {
            Username = username,
            NormalizedUsername = normalized,
            IsAdministrator = request.IsAdministrator,
        };
        user.PasswordHash = _hasher.HashPassword(user, temporaryPassword);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        await LogAsync("User.Create", user.Username, "User", user.Id, $"isAdministrator={user.IsAdministrator}", ct);
        return Ok(new CreateUserResponse(user.Id, user.Username, user.IsAdministrator, temporaryPassword));
    }

    /// <summary>Sets a user's password without needing to know their existing one (Auth.md §7),
    /// and revokes their other active sessions the same way a self-service password change does.</summary>
    [HttpPost("api/admin/users/{id:int}/set-password")]
    public async Task<IActionResult> SetPassword(int id, [FromBody] AdminSetPasswordRequest request, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();
        if (request.NewPassword.Length < AccountController.MinPasswordLength || request.NewPassword.Length > AccountController.MaxPasswordLength)
            return BadRequest($"Password must be between {AccountController.MinPasswordLength} and {AccountController.MaxPasswordLength} characters.");

        user.PasswordHash = _hasher.HashPassword(user, request.NewPassword);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        var sessions = await _db.Sessions.Where(s => s.UserId == id && s.RevokedAt == null).ToListAsync(ct);
        foreach (var session in sessions) session.RevokedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        await LogAsync("Auth.PasswordReset", user.Username, "User", user.Id, "by administrator", ct);
        return NoContent();
    }

    [HttpPost("api/admin/users/{id:int}/disable")]
    public async Task<IActionResult> DisableUser(int id, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();
        if (user.IsAdministrator && await _safety.IsSoleAdministratorAsync(id, ct))
            return BadRequest("Cannot disable the last remaining administrator.");

        user.IsDisabled = true;
        var sessions = await _db.Sessions.Where(s => s.UserId == id && s.RevokedAt == null).ToListAsync(ct);
        foreach (var session in sessions) session.RevokedAt = DateTimeOffset.UtcNow;

        await _db.SaveChangesAsync(ct);
        await LogAsync("User.Disable", user.Username, "User", user.Id, null, ct);
        return NoContent();
    }

    [HttpPost("api/admin/users/{id:int}/enable")]
    public async Task<IActionResult> EnableUser(int id, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();
        user.IsDisabled = false;
        await _db.SaveChangesAsync(ct);
        await LogAsync("User.Enable", user.Username, "User", user.Id, null, ct);
        return NoContent();
    }

    [HttpPost("api/admin/users/{id:int}/promote")]
    public async Task<IActionResult> PromoteToAdmin(int id, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();
        user.IsAdministrator = true;
        await _db.SaveChangesAsync(ct);
        await LogAsync("User.Promote", user.Username, "User", user.Id, null, ct);
        return NoContent();
    }

    [HttpPost("api/admin/users/{id:int}/demote")]
    public async Task<IActionResult> DemoteToUser(int id, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();
        if (user.IsAdministrator)
        {
            // Refuse to leave the system with zero administrators - there'd be no way back in.
            var otherAdmins = await _db.Users.CountAsync(u => u.IsAdministrator && u.Id != id, ct);
            if (otherAdmins == 0) return BadRequest("Cannot demote the last remaining administrator.");
        }
        user.IsAdministrator = false;
        await _db.SaveChangesAsync(ct);
        await LogAsync("User.Demote", user.Username, "User", user.Id, null, ct);
        return NoContent();
    }

    [HttpDelete("api/admin/users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();
        if (user.IsAdministrator && await _safety.IsSoleAdministratorAsync(id, ct))
            return BadRequest("Cannot delete the last remaining administrator.");

        var deletedUserId = user.Id;
        // No DB-level cascade for a database that had these tables hand-created by
        // DatabaseMigrations (see its raw CREATE TABLE statements) - clean up explicitly so a
        // deleted user doesn't leave orphaned identities/sessions behind.
        _db.AuthenticationIdentities.RemoveRange(_db.AuthenticationIdentities.Where(i => i.UserId == id));
        _db.Sessions.RemoveRange(_db.Sessions.Where(s => s.UserId == id));
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        await LogAsync("User.Delete", user.Username, "User", deletedUserId, null, ct);
        return NoContent();
    }

    [HttpGet("api/admin/permissions")]
    public async Task<IActionResult> ListAllPermissions(CancellationToken ct)
    {
        var perms = await _db.FolderPermissions
            .Include(p => p.AppUser)
            .Select(p => new { p.Id, p.AppUserId, Username = p.AppUser!.Username, p.FolderPath, p.Level })
            .ToListAsync(ct);
        return Ok(perms);
    }

    [HttpGet("api/admin/permissions/{userId:int}")]
    public async Task<IActionResult> GetPermissions(int userId, CancellationToken ct)
    {
        var perms = await _db.FolderPermissions.Where(p => p.AppUserId == userId).ToListAsync(ct);
        return Ok(perms);
    }

    [HttpPost("api/admin/permissions")]
    public async Task<IActionResult> GrantPermission([FromBody] GrantPermissionRequest request, CancellationToken ct)
    {
        var folderPath = request.FolderPath.Trim('/');
        var existing = await _db.FolderPermissions.FirstOrDefaultAsync(
            p => p.AppUserId == request.AppUserId && p.FolderPath == folderPath, ct);

        FolderPermission permission;
        if (existing is not null)
        {
            existing.Level = request.Level;
            permission = existing;
        }
        else
        {
            permission = new FolderPermission
            {
                AppUserId = request.AppUserId,
                FolderPath = folderPath,
                Level = request.Level
            };
            _db.FolderPermissions.Add(permission);
        }
        await _db.SaveChangesAsync(ct);
        await LogAsync("Permission.Grant", folderPath, "Permission", permission.Id, $"appUserId={request.AppUserId}, level={request.Level}", ct);
        return NoContent();
    }

    [HttpDelete("api/admin/permissions/{id:int}")]
    public async Task<IActionResult> RevokePermission(int id, CancellationToken ct)
    {
        var perm = await _db.FolderPermissions.FindAsync([id], ct);
        if (perm is null) return NotFound();
        _db.FolderPermissions.Remove(perm);
        await _db.SaveChangesAsync(ct);
        await LogAsync("Permission.Revoke", perm.FolderPath, "Permission", perm.Id, $"appUserId={perm.AppUserId}", ct);
        return NoContent();
    }

    private static string GenerateTemporaryPassword() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(18)).Replace('+', '-').Replace('/', '_').TrimEnd('=');

    private async Task LogAsync(string action, string? targetPath, string? objectType, int? objectId, string? details, CancellationToken ct)
    {
        var actingUser = await _currentUser.GetCurrentAsync(ct);
        await _audit.LogEventAsync(actingUser?.Id, action, targetPath, objectType, objectId, details, ct: ct);
    }
}
