using MarkdownHub.Api.Data.Entities.Auth;

namespace MarkdownHub.Api.Services;

/// <summary>Pre-filled templates for common providers (Auth.md §13) - convenience defaults the
/// admin UI can offer, never shared credentials. An administrator always supplies their own
/// Client ID/Secret (and, for Keycloak/Generic OIDC, the Authority).</summary>
public record ProviderPreset(string Key, string DisplayName, AuthProviderType Type, ProviderConfiguration Configuration);
