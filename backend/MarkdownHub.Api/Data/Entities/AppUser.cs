namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// The application's own user account - the single source of truth for identity and
/// authorization. Local credentials (PasswordHash) live directly on this row; external
/// provider identities are linked separately via AuthenticationIdentity so one account can
/// have any combination of a local password and multiple linked providers.
/// </summary>
public class AppUser
{
    public int Id { get; set; }
    public required string Username { get; set; }

    /// <summary>Uppercase-trimmed Username, used for case-insensitive lookup/uniqueness.</summary>
    public required string NormalizedUsername { get; set; }

    public string? Email { get; set; }

    /// <summary>Uppercase-trimmed Email, used for case-insensitive lookup. Never used as an
    /// identity/linking key by itself - see AuthenticationIdentity.</summary>
    public string? NormalizedEmail { get; set; }

    public string? DisplayName { get; set; }

    /// <summary>Null for an account with no local password (e.g. external-provider-only, or an
    /// admin-pre-provisioned account awaiting its temporary password). The initial administrator
    /// account always has one (see StartupSeeder).</summary>
    public string? PasswordHash { get; set; }

    public bool IsAdministrator { get; set; }
    public bool IsDisabled { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastLoginAt { get; set; }

    /// <summary>Folder this user wants the file tree auto-expanded to on the home page (e.g.
    /// "Campaigns/Campaign 1/Sessions"). Null means no preference - hub root only.</summary>
    public string? DefaultFolderPath { get; set; }

    public ICollection<FolderPermission> Permissions { get; set; } = new List<FolderPermission>();
    public ICollection<AuthenticationIdentity> AuthenticationIdentities { get; set; } = new List<AuthenticationIdentity>();

    public static string Normalize(string value) => value.Trim().ToUpperInvariant();
}
