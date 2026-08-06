using MarkdownHub.Api.Data.Entities.Auth;

namespace MarkdownHub.Api.Services;

public static class ProviderPresets
{
    public static readonly IReadOnlyList<ProviderPreset> All =
    [
        new("google", "Google", AuthProviderType.Oidc, new ProviderConfiguration
        {
            Authority = "https://accounts.google.com",
            Scopes = "openid profile email",
        }),
        new("github", "GitHub", AuthProviderType.OAuth2, new ProviderConfiguration
        {
            AuthorizationEndpoint = "https://github.com/login/oauth/authorize",
            TokenEndpoint = "https://github.com/login/oauth/access_token",
            UserInfoEndpoint = "https://api.github.com/user",
            Scopes = "read:user user:email",
            UserIdField = "id",
            EmailField = "email",
            NameField = "name",
        }),
        new("facebook", "Facebook", AuthProviderType.OAuth2, new ProviderConfiguration
        {
            AuthorizationEndpoint = "https://www.facebook.com/v19.0/dialog/oauth",
            TokenEndpoint = "https://graph.facebook.com/v19.0/oauth/access_token",
            UserInfoEndpoint = "https://graph.facebook.com/me?fields=id,name,email",
            Scopes = "email public_profile",
            UserIdField = "id",
            EmailField = "email",
            NameField = "name",
        }),
        new("keycloak", "Keycloak", AuthProviderType.Oidc, new ProviderConfiguration
        {
            Scopes = "openid profile email",
        }),
        new("generic-oidc", "Generic OIDC", AuthProviderType.Oidc, new ProviderConfiguration
        {
            Scopes = "openid profile email",
        }),
        new("generic-oauth2", "Generic OAuth 2.0", AuthProviderType.OAuth2, new ProviderConfiguration
        {
            Scopes = "",
            UserIdField = "sub",
        }),
    ];
}
