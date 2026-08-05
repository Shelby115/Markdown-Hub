namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// A configured OIDC identity provider the SPA can log in against and the API will accept
/// bearer tokens from. The app validates any enabled provider's tokens dynamically (see
/// Services/OidcProviderValidationService.cs) rather than assuming a single fixed authority.
/// </summary>
public class OidcProvider
{
    public int Id { get; set; }

    /// <summary>Shown on the admin page and, when more than one provider is enabled, on the login picker.</summary>
    public required string Name { get; set; }

    /// <summary>Issuer URL - must exactly match the "iss" claim of tokens it issues, and is used
    /// to fetch its OIDC discovery document / JWKS.</summary>
    public required string Authority { get; set; }

    /// <summary>Public SPA client id, sent to the frontend so it can start the authorization-code+PKCE flow.</summary>
    public required string ClientId { get; set; }

    /// <summary>Expected "aud" claim value for tokens from this provider.</summary>
    public required string Audience { get; set; }

    /// <summary>Allows an http:// authority (e.g. a docker-internal IdP) - false rejects non-https discovery.</summary>
    public bool RequireHttpsMetadata { get; set; } = true;

    public bool IsEnabled { get; set; } = true;

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
