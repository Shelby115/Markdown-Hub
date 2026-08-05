namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// Local shadow record of a Keycloak-authenticated user.
/// Keycloak is the source of truth for credentials; this table only
/// tracks app-specific state (permissions, disabled flag, audit trail).
/// </summary>
public class AppUser
{
    private const string PendingPrefix = "pending:";

    public int Id { get; set; }
    public required string KeycloakSubjectId { get; set; } // "sub" claim; a "pending:{username}" sentinel until first login
    public required string Username { get; set; }
    public string? Email { get; set; }
    public bool IsAdministrator { get; set; }
    public bool IsDisabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Folder this user wants the file tree auto-expanded to on the home page (e.g.
    /// "Campaigns/Campaign 1/Sessions"). Null means no preference - hub root only.</summary>
    public string? DefaultFolderPath { get; set; }

    public ICollection<FolderPermission> Permissions { get; set; } = new List<FolderPermission>();

    public bool IsPending => KeycloakSubjectId.StartsWith(PendingPrefix);

    /// <summary>Sentinel KeycloakSubjectId for a user an admin pre-provisioned, ahead of their first login.
    /// Unique per-username (Username already has a unique index), so it can't collide across placeholders.</summary>
    public static string PendingSubjectId(string username) => PendingPrefix + username;
}
