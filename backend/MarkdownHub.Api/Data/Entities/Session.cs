namespace MarkdownHub.Api.Data.Entities;

/// <summary>
/// A server-tracked login session. The app issues a bearer JWT carrying this row's Id as its
/// "sid" claim (see Services/AppTokenService.cs); every authenticated request looks the session
/// up and rejects the token if it's missing, expired, or revoked. This is what makes an
/// otherwise-stateless JWT individually revocable - by the user themselves (Account > Sessions)
/// or by an administrator - without needing cookie-based auth.
/// </summary>
public class Session
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public int UserId { get; set; }
    public AppUser? User { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? RevokedAt { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }

    public bool IsActive => RevokedAt is null && DateTimeOffset.UtcNow < ExpiresAt;
}
