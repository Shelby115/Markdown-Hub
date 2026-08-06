namespace MarkdownHub.Api.Controllers.Auth;

public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, DateTimeOffset ExpiresAt);
public record ExternalLinkStartResponse(string RedirectUrl);
