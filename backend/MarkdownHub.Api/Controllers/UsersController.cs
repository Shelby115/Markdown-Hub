using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

public record GrantPermissionRequest(int AppUserId, string FolderPath, PermissionLevel Level);
public record CreateUserRequest(string Username, bool IsAdministrator = false);

/// <summary>
/// Administrator-only user and permission management. Authorization here is enforced
/// via the "IsAdministratorOnly" policy (see Program.cs), which checks the local
/// AppUser.IsAdministrator flag - Keycloak roles are NOT trusted directly for this,
/// since demoting/promoting admin status is an app-level concern tracked in our DB.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Policy = "RequireAdministrator")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly AuditLogService _audit;

    public UsersController(AppDbContext db, CurrentUserService currentUser, AuditLogService audit)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    [HttpGet("users")]
    public async Task<IActionResult> ListUsers(CancellationToken ct)
    {
        var users = await _db.Users.ToListAsync(ct);
        return Ok(users.Select(u => new
        {
            u.Id, u.Username, u.Email, u.IsAdministrator, u.IsDisabled, u.CreatedAt, u.LastLoginAt, u.IsPending
        }));
    }

    [HttpPost("users")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest request, CancellationToken ct)
    {
        var username = request.Username.Trim();
        if (string.IsNullOrEmpty(username)) return BadRequest("Username is required.");

        var exists = await _db.Users.AnyAsync(u => u.Username == username, ct);
        if (exists) return Conflict("A user with that username already exists.");

        // Created without a real Keycloak subject - it's claimed automatically the first time
        // someone with a matching Keycloak username signs in (see CurrentUserService).
        var user = new AppUser
        {
            KeycloakSubjectId = AppUser.PendingSubjectId(username),
            Username = username,
            IsAdministrator = request.IsAdministrator
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);
        await LogAsync("User.Create", user.Username, "User", user.Id, $"isAdministrator={user.IsAdministrator}", ct);
        return Ok(new
        {
            user.Id, user.Username, user.Email, user.IsAdministrator, user.IsDisabled, user.CreatedAt, user.LastLoginAt, user.IsPending
        });
    }

    [HttpPost("users/{id:int}/disable")]
    public async Task<IActionResult> DisableUser(int id, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();
        user.IsDisabled = true;
        await _db.SaveChangesAsync(ct);
        await LogAsync("User.Disable", user.Username, "User", user.Id, null, ct);
        return NoContent();
    }

    [HttpPost("users/{id:int}/enable")]
    public async Task<IActionResult> EnableUser(int id, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();
        user.IsDisabled = false;
        await _db.SaveChangesAsync(ct);
        await LogAsync("User.Enable", user.Username, "User", user.Id, null, ct);
        return NoContent();
    }

    [HttpPost("users/{id:int}/promote")]
    public async Task<IActionResult> PromoteToAdmin(int id, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();
        user.IsAdministrator = true;
        await _db.SaveChangesAsync(ct);
        await LogAsync("User.Promote", user.Username, "User", user.Id, null, ct);
        return NoContent();
    }

    [HttpPost("users/{id:int}/demote")]
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

    [HttpDelete("users/{id:int}")]
    public async Task<IActionResult> DeleteUser(int id, CancellationToken ct)
    {
        var user = await _db.Users.FindAsync([id], ct);
        if (user is null) return NotFound();
        var deletedUserId = user.Id;
        _db.Users.Remove(user);
        await _db.SaveChangesAsync(ct);
        await LogAsync("User.Delete", user.Username, "User", deletedUserId, null, ct);
        return NoContent();
    }

    [HttpGet("permissions")]
    public async Task<IActionResult> ListAllPermissions(CancellationToken ct)
    {
        var perms = await _db.FolderPermissions
            .Include(p => p.AppUser)
            .Select(p => new { p.Id, p.AppUserId, Username = p.AppUser!.Username, p.FolderPath, p.Level })
            .ToListAsync(ct);
        return Ok(perms);
    }

    [HttpGet("permissions/{userId:int}")]
    public async Task<IActionResult> GetPermissions(int userId, CancellationToken ct)
    {
        var perms = await _db.FolderPermissions.Where(p => p.AppUserId == userId).ToListAsync(ct);
        return Ok(perms);
    }

    [HttpPost("permissions")]
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

    [HttpDelete("permissions/{id:int}")]
    public async Task<IActionResult> RevokePermission(int id, CancellationToken ct)
    {
        var perm = await _db.FolderPermissions.FindAsync([id], ct);
        if (perm is null) return NotFound();
        _db.FolderPermissions.Remove(perm);
        await _db.SaveChangesAsync(ct);
        await LogAsync("Permission.Revoke", perm.FolderPath, "Permission", perm.Id, $"appUserId={perm.AppUserId}", ct);
        return NoContent();
    }

    private async Task LogAsync(string action, string? targetPath, string? objectType, int? objectId, string? details, CancellationToken ct)
    {
        var actingUser = await _currentUser.GetOrCreateAsync(ct);
        await _audit.LogEventAsync(actingUser?.Id, action, targetPath, objectType, objectId, details, ct: ct);
    }
}
