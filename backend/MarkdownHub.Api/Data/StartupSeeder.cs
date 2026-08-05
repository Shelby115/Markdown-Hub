using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Data;

/// <summary>
/// One-time, create-only seeding that runs every startup but only ever inserts rows that don't
/// exist yet - never mutates an existing user's role or an already-configured OIDC provider, so
/// changing these config values later doesn't silently override admin/DB-managed state.
/// </summary>
public static class StartupSeeder
{
    public static async Task SeedAdminAsync(AppDbContext db, IConfiguration configuration, CancellationToken ct = default)
    {
        var seedUsername = configuration["Admin:SeedUsername"]?.Trim();
        if (string.IsNullOrEmpty(seedUsername)) return;

        var exists = await db.Users.AnyAsync(u => u.Username == seedUsername, ct);
        if (exists) return;

        // Same shape as UsersController.CreateUser - claimed automatically the first time
        // someone with a matching OIDC username signs in (see CurrentUserService).
        db.Users.Add(new AppUser
        {
            KeycloakSubjectId = AppUser.PendingSubjectId(seedUsername),
            Username = seedUsername,
            IsAdministrator = true
        });
        await db.SaveChangesAsync(ct);
    }

    /// <summary>
    /// Inserts one OIDC provider from the "OidcDefault" config section the first time the app
    /// ever boots against a database with no providers yet - after that the DB is authoritative
    /// and managed via the admin UI, so this never runs again once a provider exists (even if the
    /// underlying env vars stay set or change).
    /// </summary>
    public static async Task SeedDefaultOidcProviderAsync(AppDbContext db, IConfiguration configuration, CancellationToken ct = default)
    {
        if (await db.OidcProviders.AnyAsync(ct)) return;

        var authority = configuration["OidcDefault:Authority"]?.Trim();
        var clientId = configuration["OidcDefault:ClientId"]?.Trim();
        var audience = configuration["OidcDefault:Audience"]?.Trim();
        if (string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(audience))
            return; // Nothing configured yet - a from-scratch install with no IdP set up.

        db.OidcProviders.Add(new OidcProvider
        {
            Name = configuration["OidcDefault:Name"]?.Trim() is { Length: > 0 } name ? name : "Default",
            Authority = authority,
            ClientId = clientId,
            Audience = audience,
            RequireHttpsMetadata = configuration.GetValue("OidcDefault:RequireHttpsMetadata", true),
            IsEnabled = true
        });
        await db.SaveChangesAsync(ct);
    }
}
