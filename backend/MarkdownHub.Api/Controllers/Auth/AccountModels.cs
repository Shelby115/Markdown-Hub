namespace MarkdownHub.Api.Controllers.Auth;

public record SetDefaultFolderRequest(string? FolderPath);
public record ChangePasswordRequest(string? CurrentPassword, string NewPassword, string ConfirmNewPassword);
public record AuthMethodsResponse(bool HasPassword, IReadOnlyList<LinkedIdentityResponse> LinkedIdentities);
public record LinkedIdentityResponse(int Id, int ProviderId, string ProviderName, string ProviderDisplayName, DateTimeOffset CreatedAt, DateTimeOffset? LastUsedAt);
public record SessionResponse(Guid Id, DateTimeOffset CreatedAt, DateTimeOffset ExpiresAt, DateTimeOffset LastActivityAt, string? UserAgent, string? IpAddress, bool IsCurrent);
