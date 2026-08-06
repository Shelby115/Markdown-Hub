using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Controllers.Auth;

public record GrantPermissionRequest(int AppUserId, string FolderPath, PermissionLevel Level);
public record CreateUserRequest(string Username, string? TemporaryPassword, bool IsAdministrator = false);
public record CreateUserResponse(int Id, string Username, bool IsAdministrator, string TemporaryPassword);
public record AdminSetPasswordRequest(string NewPassword);
