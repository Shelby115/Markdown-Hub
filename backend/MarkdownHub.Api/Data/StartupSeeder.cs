using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Data;

/// <summary>
/// One-time, create-only seeding that runs every startup but only ever inserts rows that don't
/// exist yet - never mutates an existing user's password/role or an already-configured provider,
/// so changing these config values later doesn't silently override admin/DB-managed state
/// (Auth.md §8: "must not continuously compare the database password against ADMIN_PASSWORD").
/// </summary>
public static class StartupSeeder
{
    /// <summary>
    /// Bootstraps the initial administrator account from ADMIN_USERNAME / ADMIN_PASSWORD (or
    /// ADMIN_PASSWORD_FILE). Deliberately waits until *both* a username and a password are
    /// configured before creating anything - Auth.md §3 requires the initial administrator
    /// account to always have a password, and seeding a passwordless placeholder that a later
    /// boot might never get around to completing would risk a deployment with an admin username
    /// reserved but no way to actually authenticate as them.
    /// </summary>
    public static async Task SeedAdminAsync(AppDbContext db, IConfiguration configuration, IPasswordHasher<AppUser> hasher, CancellationToken ct = default)
    {
        var username = configuration["Admin:Username"]?.Trim();
        if (string.IsNullOrEmpty(username)) return;

        var password = ResolveAdminPassword(configuration);
        if (string.IsNullOrEmpty(password)) return;

        var normalized = AppUser.Normalize(username);
        if (await db.Users.AnyAsync(u => u.NormalizedUsername == normalized, ct)) return;

        var user = new AppUser
        {
            Username = username,
            NormalizedUsername = normalized,
            IsAdministrator = true,
        };
        user.PasswordHash = hasher.HashPassword(user, password);
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
    }

    private static string? ResolveAdminPassword(IConfiguration configuration)
    {
        var passwordFile = configuration["Admin:PasswordFile"]?.Trim();
        if (!string.IsNullOrEmpty(passwordFile) && File.Exists(passwordFile))
            return File.ReadAllText(passwordFile).Trim();
        return configuration["Admin:Password"]?.Trim();
    }

    /// <summary>
    /// Inserts one external provider from the "OidcDefault" config section the first time the
    /// app ever boots against a database with no providers yet - after that the DB is
    /// authoritative and managed via the admin UI, so this never runs again once a provider
    /// exists (even if the underlying env vars stay set or change). Requires a client secret
    /// (unlike the old SPA-driven public-client flow) since the server now performs the
    /// authorization-code exchange itself.
    /// </summary>
    public static async Task SeedDefaultProviderAsync(AppDbContext db, IConfiguration configuration, ProviderSecretProtector secretProtector, CancellationToken ct = default)
    {
        if (await db.AuthenticationProviders.AnyAsync(ct)) return;

        var authority = configuration["OidcDefault:Authority"]?.Trim();
        var clientId = configuration["OidcDefault:ClientId"]?.Trim();
        var clientSecret = ResolveDefaultProviderSecret(configuration);
        if (string.IsNullOrEmpty(authority) || string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret))
            return; // nothing fully configured yet - a from-scratch install with no external provider set up

        var displayName = configuration["OidcDefault:Name"]?.Trim() is { Length: > 0 } name ? name : "Default";
        var config = new ProviderConfiguration
        {
            Authority = authority,
            RequireHttpsMetadata = configuration.GetValue("OidcDefault:RequireHttpsMetadata", true),
            Audience = configuration["OidcDefault:Audience"]?.Trim(),
            AutoProvision = AutoProvisionPolicy.Allow,
        };

        db.AuthenticationProviders.Add(new AuthenticationProvider
        {
            Name = ProviderNameSlug.Create(displayName),
            DisplayName = displayName,
            Type = AuthProviderType.Oidc,
            ClientId = clientId,
            ClientSecretProtected = secretProtector.Protect(clientSecret),
            ConfigurationJson = JsonSerializer.Serialize(config),
            Enabled = true,
        });
        await db.SaveChangesAsync(ct);
    }

    private static string? ResolveDefaultProviderSecret(IConfiguration configuration)
    {
        var secretFile = configuration["OidcDefault:ClientSecretFile"]?.Trim();
        if (!string.IsNullOrEmpty(secretFile) && File.Exists(secretFile))
            return File.ReadAllText(secretFile).Trim();
        return configuration["OidcDefault:ClientSecret"]?.Trim();
    }
}
