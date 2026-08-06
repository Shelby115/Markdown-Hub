namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// Links an application user to one external provider identity. A user may have any number of
/// these alongside (or instead of) a local password - see AppUser.PasswordHash. The provider's
/// own subject/user id (Subject) is the authoritative, immutable key for that external identity;
/// email is never used as the linking key (see Auth.md §16).
/// </summary>
public class AuthenticationIdentity
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public int AuthenticationProviderId { get; set; }
    public AuthenticationProvider? Provider { get; set; }

    /// <summary>The provider's stable subject/user id (OIDC "sub", or the provider API's user id
    /// for a plain OAuth 2.0 provider).</summary>
    public required string Subject { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? LastUsedAt { get; set; }
}
