using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Centralizes the "don't let an administrator accidentally lock themselves out" safety
/// invariants from Auth.md §10/§17/§31.6: a user's last usable authentication method (local
/// password or a linked external identity) can't be removed, and a provider can't be
/// disabled/deleted if doing so would leave the last remaining administrator with none left.
/// </summary>
public class AccountSafetyService
{
    private readonly AppDbContext _db;

    public AccountSafetyService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<int> CountUsableAuthMethodsAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        var identityCount = await _db.AuthenticationIdentities.CountAsync(i => i.UserId == userId, ct);
        return (user?.PasswordHash is not null ? 1 : 0) + identityCount;
    }

    public async Task<bool> IsSoleAdministratorAsync(int userId, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([userId], ct);
        if (user is not { IsAdministrator: true, IsDisabled: false }) return false;
        var otherAdmins = await _db.Users.CountAsync(u => u.IsAdministrator && !u.IsDisabled && u.Id != userId, ct);
        return otherAdmins == 0;
    }

    /// <summary>True if removing one authentication method from this user right now would
    /// leave them with zero usable methods (local password + linked identities combined).
    /// Applies to every user, not just administrators - Auth.md §10 states the invariant
    /// generally ("the application must prevent an administrator from accidentally removing
    /// their final authentication method"), and there's no reason a non-admin should be allowed
    /// to lock themselves out either.</summary>
    public async Task<bool> WouldRemovalLeaveNoUsableMethodAsync(int userId, CancellationToken ct = default)
    {
        var remaining = await CountUsableAuthMethodsAsync(userId, ct) - 1;
        return remaining <= 0;
    }

    /// <summary>How many distinct application users currently have an identity linked through
    /// this provider - shown before an admin deletes it (Auth.md §17).</summary>
    public Task<int> CountUsersUsingProviderAsync(int providerId, CancellationToken ct = default) =>
        _db.AuthenticationIdentities.Where(i => i.AuthenticationProviderId == providerId)
            .Select(i => i.UserId).Distinct().CountAsync(ct);

    /// <summary>True if disabling/deleting this provider would leave the sole remaining
    /// administrator with no usable authentication method at all. Deliberately narrower than
    /// "is this the last enabled provider globally" - an administrator who also has a local
    /// password, or another linked provider, is unaffected (see Auth.md §10's own example).</summary>
    public async Task<bool> WouldProviderRemovalStrandLastAdministratorAsync(int providerId, CancellationToken ct = default)
    {
        var admins = await _db.Users.Where(u => u.IsAdministrator && !u.IsDisabled).ToListAsync(ct);
        if (admins.Count != 1) return false;

        var admin = admins[0];
        if (admin.PasswordHash is not null) return false;

        var adminProviderIds = await _db.AuthenticationIdentities
            .Where(i => i.UserId == admin.Id)
            .Select(i => i.AuthenticationProviderId)
            .ToListAsync(ct);
        if (!adminProviderIds.Contains(providerId)) return false;

        return adminProviderIds.All(id => id == providerId);
    }
}
