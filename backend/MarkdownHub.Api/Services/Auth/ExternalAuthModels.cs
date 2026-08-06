namespace MarkdownHub.Api.Services;

public enum AuthIntent { Login, Link }

/// <summary>Encrypted round-trip payload carried in the OAuth/OIDC "state" parameter - validates
/// the callback belongs to a request this server actually issued (Auth.md §20) and carries the
/// PKCE verifier and linking intent across the redirect, without needing server-side storage.</summary>
public record ExternalAuthState(
    string ProviderName,
    AuthIntent Intent,
    int? LinkUserId,
    string CodeVerifier,
    string OidcNonce,
    string ReturnOrigin,
    DateTimeOffset IssuedAt);

/// <summary>Normalized identity claims extracted from a provider, regardless of whether they
/// came from an OIDC id_token or a plain OAuth2 userinfo response.</summary>
public record ExternalIdentity(string Subject, string? Email, string? Name);
