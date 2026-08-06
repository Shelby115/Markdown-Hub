using MarkdownHub.Api.Data.Entities.Auth;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Provider-type-specific settings stored as JSON in AuthenticationProvider.ConfigurationJson
/// (kept out of dedicated columns since OIDC and OAuth2 providers need different fields - see
/// Auth.md §25's "Configuration" schema entry). Deserialized/serialized via
/// System.Text.Json; unknown/omitted fields fall back to sane defaults.
/// </summary>
public class ProviderConfiguration
{
    // --- OIDC ---
    /// <summary>Issuer URL - required for Oidc providers. Its discovery document is fetched at
    /// "{Authority}/.well-known/openid-configuration".</summary>
    public string? Authority { get; set; }

    /// <summary>Allows an http:// authority (e.g. a docker-internal IdP) - false rejects
    /// non-https discovery.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    /// <summary>Expected "aud" claim value in the provider's id_token, if it validates one
    /// beyond the client id itself. Optional - many providers use ClientId as the audience.</summary>
    public string? Audience { get; set; }

    // --- OAuth2 (generic; also usable to override discovery-derived OIDC endpoints) ---
    public string? AuthorizationEndpoint { get; set; }
    public string? TokenEndpoint { get; set; }
    public string? UserInfoEndpoint { get; set; }

    /// <summary>Space-delimited scopes requested during authorization.</summary>
    public string Scopes { get; set; } = "openid profile email";

    /// <summary>JSON property name in the userinfo response holding the provider's stable
    /// subject/user id. For OIDC this is normally taken from the id_token's "sub" claim instead
    /// and this field is only consulted as a fallback for pure-OAuth2 providers.</summary>
    public string UserIdField { get; set; } = "sub";

    public string? EmailField { get; set; } = "email";
    public string? NameField { get; set; } = "name";

    public AutoProvisionPolicy AutoProvision { get; set; } = AutoProvisionPolicy.Allow;
}
