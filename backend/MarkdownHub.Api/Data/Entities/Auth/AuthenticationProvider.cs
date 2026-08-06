namespace MarkdownHub.Api.Data.Entities.Auth;

public enum AuthProviderType
{
    Oidc = 0,
    OAuth2 = 1,
}

/// <summary>What happens when someone authenticates through this provider for the first time
/// (no AuthenticationIdentity yet matches their subject) and no application account is linked.</summary>
public enum AutoProvisionPolicy
{
    /// <summary>Create a new, immediately-usable application account.</summary>
    Allow = 0,

    /// <summary>Create the account but leave it disabled until an administrator enables it.</summary>
    RequireApproval = 1,

    /// <summary>Refuse the sign-in; only accounts that already have this identity linked
    /// (via the self-service linking flow) may authenticate through this provider.</summary>
    Disabled = 2,
}

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
