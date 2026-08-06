namespace MarkdownHub.Api.Data.Entities.Auth;

/// <summary>
/// A configured external identity provider (OIDC or OAuth 2.0) users may optionally authenticate
/// or link through. Never the application's sole identity system - see AppUser.PasswordHash for
/// the always-available local login path, and AuthenticationIdentity for how a provider identity
/// attaches to an application account.
/// </summary>
public class AuthenticationProvider
{
    public int Id { get; set; }

    /// <summary>Stable URL-safe slug (e.g. "keycloak", "google") used in routes/redirect URIs.</summary>
    public required string Name { get; set; }

    /// <summary>Shown on the admin page and the sign-in picker.</summary>
    public required string DisplayName { get; set; }

    public AuthProviderType Type { get; set; }

    /// <summary>Public client id - not a secret, safe to expose if ever needed.</summary>
    public required string ClientId { get; set; }

    /// <summary>The provider's client secret, encrypted at rest via ASP.NET Core Data Protection
    /// (see Services/ProviderSecretProtector.cs). Never returned to the browser in plaintext.</summary>
    public string? ClientSecretProtected { get; set; }

    /// <summary>Provider-type-specific settings (endpoints, scopes, audience, field mappings,
    /// auto-provisioning policy) serialized as JSON - see Services/ProviderConfiguration.cs.</summary>
    public required string ConfigurationJson { get; set; }

    public bool Enabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
}
