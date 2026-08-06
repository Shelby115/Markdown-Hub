using MarkdownHub.Api.Data.Entities.Auth;

namespace MarkdownHub.Api.Controllers.Auth;

public record AuthProviderResponse(int Id, string Name, string DisplayName, AuthProviderType Type);
