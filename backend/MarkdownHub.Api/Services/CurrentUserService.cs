using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Resolves the local AppUser row for the caller's Keycloak "sub" claim, creating a
/// shadow record on first login. Never trusts a client-supplied user id.
/// </summary>
public class CurrentUserService
{
    private readonly AppDbContext _db;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserService(AppDbContext db, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<AppUser?> GetOrCreateAsync(CancellationToken ct = default)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub");
        if (string.IsNullOrEmpty(subject)) return null;

        var user = await _db.Users.FirstOrDefaultAsync(u => u.KeycloakSubjectId == subject, ct);
        if (user is null)
        {
            var username = principal!.FindFirstValue("preferred_username") ?? subject;
            var email = principal.FindFirstValue(ClaimTypes.Email);

            // An admin may have pre-provisioned this username (to assign permissions/role ahead of
            // time) before this person ever logged in - claim that placeholder row instead of
            // creating a duplicate, so anything already granted to it carries over.
            var pendingSubjectId = AppUser.PendingSubjectId(username);
            var pending = await _db.Users.FirstOrDefaultAsync(u => u.KeycloakSubjectId == pendingSubjectId, ct);
            if (pending is not null)
            {
                pending.KeycloakSubjectId = subject;
                pending.Email ??= email;
                user = pending;
            }
            else
            {
                user = new AppUser
                {
                    KeycloakSubjectId = subject,
                    Username = username,
                    Email = email,
                    // First user ever created becomes an administrator so the system is bootstrapped.
                    IsAdministrator = !await _db.Users.AnyAsync(ct)
                };
                _db.Users.Add(user);
            }
            await _db.SaveChangesAsync(ct);
        }

        if (user.IsDisabled) return null;

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        return user;
    }
}
