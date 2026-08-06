using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Resolves the local AppUser row for the caller's "sub" claim, which is always the app's own
/// AppUser.Id (an int, minted by AppTokenService) - never an external provider's subject.
/// Accounts are always created explicitly (local registration/admin pre-provisioning, or the
/// external-login callback's find-or-create step in Controllers/Auth/AuthController.cs), so unlike
/// the old OIDC-only model this never needs to create a row on first sight of a token.
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

    public async Task<AppUser?> GetCurrentAsync(CancellationToken ct = default)
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? principal?.FindFirstValue("sub");
        if (!int.TryParse(subject, out var userId)) return null;

        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null || user.IsDisabled) return null;
        return user;
    }
}
