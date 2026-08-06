using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Data.Entities.Auth;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Issues and persists app-owned login sessions. The app is always the JWT issuer - even after a
/// successful external OIDC/OAuth2 login, the provider's own tokens are exchanged server-side and
/// never handed to the browser (see Controllers/Auth/AuthController.cs); the browser only ever holds a
/// token this service minted. Every issued token carries a "sid" claim tied to a Session row, so
/// sessions stay individually revocable despite the transport being a bearer JWT (see Program.cs's
/// OnTokenValidated for the enforcement side).
/// </summary>
public class AppTokenService
{
    public const string Issuer = "markdown-hub";
    public const string Audience = "markdown-hub";
    private const string SigningKeySettingKey = "Auth.Jwt.SigningKey";

    private readonly AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AppTokenService(AppDbContext db, IConfiguration config, IHttpContextAccessor httpContextAccessor)
    {
        _db = db;
        _config = config;
        _httpContextAccessor = httpContextAccessor;
    }

    public TimeSpan SessionLifetime =>
        TimeSpan.FromHours(_config.GetValue<double?>("Sessions:LifetimeHours") ?? 24 * 7);

    /// <summary>Creates a new Session row and returns a signed JWT carrying it. Callers are
    /// responsible for having already authenticated the user (local password check, or a
    /// completed external provider exchange) - this method does no authentication itself.</summary>
    public async Task<(string Token, Session Session)> IssueAsync(AppUser user, CancellationToken ct = default)
    {
        var http = _httpContextAccessor.HttpContext;
        var session = new Session
        {
            UserId = user.Id,
            ExpiresAt = DateTimeOffset.UtcNow.Add(SessionLifetime),
            UserAgent = Truncate(http?.Request.Headers.UserAgent.ToString(), 256),
            IpAddress = http?.Connection.RemoteIpAddress?.ToString(),
        };
        _db.Sessions.Add(session);
        await _db.SaveChangesAsync(ct);

        var keyBytes = await GetSigningKeyAsync(ct);
        var credentials = new SigningCredentials(new SymmetricSecurityKey(keyBytes), SecurityAlgorithms.HmacSha256);
        var claims = new[]
        {
            new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new Claim("preferred_username", user.Username),
            new Claim("sid", session.Id.ToString()),
        };
        var token = new JwtSecurityToken(Issuer, Audience, claims,
            expires: session.ExpiresAt.UtcDateTime, signingCredentials: credentials);
        return (new JwtSecurityTokenHandler().WriteToken(token), session);
    }

    /// <summary>Resolves the symmetric signing key: an explicit "Jwt:SigningKey" config override
    /// (env var JWT_SIGNING_KEY) takes precedence for multi-instance deployments that need a
    /// shared key; otherwise a key is generated once and persisted in the Settings table (same
    /// DB-backed-setting pattern as the AI model/history retention settings) so it survives
    /// restarts without requiring any configuration for the common single-instance case.</summary>
    public async Task<byte[]> GetSigningKeyAsync(CancellationToken ct = default)
    {
        var overrideValue = _config["Jwt:SigningKey"];
        if (!string.IsNullOrWhiteSpace(overrideValue)) return DecodeOrDeriveKey(overrideValue);

        var setting = await _db.Settings.FirstOrDefaultAsync(s => s.Key == SigningKeySettingKey, ct);
        if (setting?.Value is { Length: > 0 } existing) return Convert.FromBase64String(existing);

        var generated = RandomNumberGenerator.GetBytes(32);
        _db.Settings.Add(new AppSetting { Key = SigningKeySettingKey, Value = Convert.ToBase64String(generated) });
        await _db.SaveChangesAsync(ct);
        return generated;
    }

    private static byte[] DecodeOrDeriveKey(string configuredValue)
    {
        // Accept either a base64-encoded key, or fall back to hashing an arbitrary configured
        // string into a fixed-length key so operators aren't forced to hand-generate base64.
        try { return Convert.FromBase64String(configuredValue); }
        catch (FormatException) { return SHA256.HashData(Encoding.UTF8.GetBytes(configuredValue)); }
    }

    private static string? Truncate(string? value, int maxLength) =>
        string.IsNullOrEmpty(value) || value.Length <= maxLength ? value : value[..maxLength];
}
