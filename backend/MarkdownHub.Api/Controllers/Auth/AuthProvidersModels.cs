using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.Auth;

public record AuthenticationProviderResponse(
    int Id, string Name, string DisplayName, AuthProviderType Type, string ClientId,
    bool HasClientSecret, ProviderConfiguration Configuration, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int UsersUsingProvider);

public record CreateAuthenticationProviderRequest(
    string Name, string DisplayName, AuthProviderType Type, string ClientId,
    string? ClientSecret, ProviderConfiguration Configuration);

public record UpdateAuthenticationProviderRequest(
    string DisplayName, AuthProviderType Type, string ClientId,
    string? ClientSecret, ProviderConfiguration Configuration);

public record ProviderPresetResponse(string Key, string DisplayName, AuthProviderType Type, ProviderConfiguration Configuration);
