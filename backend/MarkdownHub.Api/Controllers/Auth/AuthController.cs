using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.Auth;

/// <summary>
/// Local login and the server-driven OIDC/OAuth2 authorization-code flow (Auth.md §5/§11/§12).
/// The app is always the confidential client here - provider tokens and client secrets are
/// exchanged/held server-side and never reach the browser; the browser only ever receives a
/// token this app minted itself (see AppTokenService).
/// </summary>
[ApiController]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IPasswordHasher<AppUser> _hasher;
    private readonly AppTokenService _tokens;
    private readonly ExternalAuthService _external;
    private readonly AuditLogService _audit;
    private readonly IConfiguration _config;
    private readonly ILogger<AuthController> _logger;

    public AuthController(AppDbContext db, IPasswordHasher<AppUser> hasher, AppTokenService tokens,
        ExternalAuthService external, AuditLogService audit, IConfiguration config, ILogger<AuthController> logger)
    {
        _db = db;
        _hasher = hasher;
        _tokens = tokens;
        _external = external;
        _audit = audit;
        _logger = logger;
        _config = config;
    }

    private string? RemoteIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpPost("api/auth/login")]
    [AllowAnonymous]
    [EnableRateLimiting("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken ct)
    {
        var username = (request.Username ?? "").Trim();
        var password = request.Password ?? "";
        var normalized = AppUser.Normalize(username);
        var user = await _db.Users.FirstOrDefaultAsync(u => u.NormalizedUsername == normalized, ct);

        if (user is null || user.PasswordHash is null || user.IsDisabled)
        {
            // Run password-hashing work even on a miss so response timing doesn't itself reveal
            // whether the username exists (Auth.md §21).
            _hasher.HashPassword(new AppUser { Username = "_", NormalizedUsername = "_" }, password);
            await _audit.LogGroupedAsync("Auth.LoginFailed", username, "Auth", RemoteIp, "invalid credentials", TimeSpan.FromMinutes(5), ct);
            return Unauthorized(new { message = "Invalid username or password." });
        }

        var verification = _hasher.VerifyHashedPassword(user, user.PasswordHash, password);
        if (verification == PasswordVerificationResult.Failed)
        {
            await _audit.LogGroupedAsync("Auth.LoginFailed", username, "Auth", RemoteIp, "invalid credentials", TimeSpan.FromMinutes(5), ct);
            return Unauthorized(new { message = "Invalid username or password." });
        }

        if (verification == PasswordVerificationResult.SuccessRehashNeeded)
        {
            user.PasswordHash = _hasher.HashPassword(user, password);
        }

        user.LastLoginAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);

        var (token, session) = await _tokens.IssueAsync(user, ct);
        await _audit.LogEventAsync(user.Id, "Auth.Login", user.Username, "Auth", user.Id, "local", ct: ct);
        return Ok(new LoginResponse(token, session.ExpiresAt));
    }

    /// <summary>Starts the login-intent authorization redirect for an enabled provider.</summary>
    [HttpGet("api/auth/external/{providerName}")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalLogin(string providerName, [FromQuery] string? returnOrigin, CancellationToken ct)
    {
        var provider = await _db.AuthenticationProviders.FirstOrDefaultAsync(p => p.Name == providerName && p.Enabled, ct);
        if (provider is null)
        {
            return NotFound(new { message = "Unknown or disabled provider." });
        }

        var redirectUri = BuildCallbackUri(providerName);
        var origin = ResolveReturnOrigin(returnOrigin);
        try
        {
            var url = await _external.BuildAuthorizationUrlAsync(provider, redirectUri, AuthIntent.Login, null, origin, ct);
            return Redirect(url);
        }
        catch (InvalidOperationException ex)
        {
            _logger.LogWarning(ex, "Failed to start external login for provider '{ProviderName}'", providerName);
            return RedirectToFrontendError(origin, ex.Message);
        }
    }

    /// <summary>Starts the link-intent authorization redirect for the already-authenticated
    /// caller. Returns the URL as JSON (rather than redirecting directly) since this is an
    /// authenticated fetch call, not a page navigation - the frontend performs the actual
    /// browser redirect itself.</summary>
    [HttpPost("api/auth/external/{providerName}/link-start")]
    [Authorize]
    public async Task<IActionResult> ExternalLinkStart(string providerName, [FromQuery] string? returnOrigin, CancellationToken ct)
    {
        var subject = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? User.FindFirst("sub")?.Value;
        if (!int.TryParse(subject, out var userId))
        {
            return Unauthorized();
        }

        var provider = await _db.AuthenticationProviders.FirstOrDefaultAsync(p => p.Name == providerName && p.Enabled, ct);
        if (provider is null)
        {
            return NotFound(new { message = "Unknown or disabled provider." });
        }

        var redirectUri = BuildCallbackUri(providerName);
        var origin = ResolveReturnOrigin(returnOrigin);
        var url = await _external.BuildAuthorizationUrlAsync(provider, redirectUri, AuthIntent.Link, userId, origin, ct);
        return Ok(new ExternalLinkStartResponse(url));
    }

    [HttpGet("api/auth/external/{providerName}/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> ExternalCallback(string providerName,
        [FromQuery] string? code, [FromQuery] string? state, [FromQuery] string? error, CancellationToken ct)
    {
        var origin = ResolveReturnOrigin(null); // fall back origin until state (which carries the real one) is decoded
        try
        {
            if (!string.IsNullOrEmpty(error))
            {
                throw new InvalidOperationException($"The provider reported an error: {error}");
            }

            if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(state))
            {
                throw new InvalidOperationException("Sign-in response was incomplete.");
            }

            var authState = _external.UnprotectState(state);
            origin = ResolveReturnOrigin(authState.ReturnOrigin);
            if (!string.Equals(authState.ProviderName, providerName, StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Sign-in request did not match the callback provider.");
            }

            var provider = await _db.AuthenticationProviders.FirstOrDefaultAsync(p => p.Name == providerName && p.Enabled, ct);
            if (provider is null)
            {
                throw new InvalidOperationException("This provider is no longer enabled.");
            }

            var redirectUri = BuildCallbackUri(providerName);
            var identity = await _external.ExchangeCodeAsync(provider, code, redirectUri, authState, ct);

            var user = authState.Intent == AuthIntent.Link
                ? await LinkIdentityAsync(provider, identity, authState.LinkUserId!.Value, ct)
                : await FindOrProvisionUserAsync(provider, identity, ct);

            if (user is null)
            {
                throw new InvalidOperationException("Sign-in was not completed - the account is disabled or requires administrator approval.");
            }

            var (token, _) = await _tokens.IssueAsync(user, ct);
            await _audit.LogEventAsync(user.Id, "Auth.Login", user.Username, "Auth", user.Id, provider.Name, ct: ct);
            return Redirect($"{origin}/auth/callback#token={Uri.EscapeDataString(token)}");
        }
        catch (InvalidOperationException ex)
        {
            // The message shown to the user is deliberately generic/user-facing - log the full
            // exception (including the inner cause, e.g. the underlying token-validation
            // failure) here so a misconfigured provider is actually diagnosable from the logs
            // instead of requiring guesswork.
            _logger.LogWarning(ex, "External auth callback failed for provider '{ProviderName}'", providerName);
            return RedirectToFrontendError(origin, ex.Message);
        }
    }

    private async Task<AppUser?> LinkIdentityAsync(AuthenticationProvider provider, ExternalIdentity identity, int userId, CancellationToken ct)
    {
        var existing = await _db.AuthenticationIdentities
            .FirstOrDefaultAsync(i => i.AuthenticationProviderId == provider.Id && i.Subject == identity.Subject, ct);
        if (existing is not null && existing.UserId != userId)
        {
            throw new InvalidOperationException("This provider identity is already linked to a different account.");
        }

        var user = await _db.Users.FindAsync([userId], ct);
        if (user is null || user.IsDisabled)
        {
            return null;
        }

        if (existing is null)
        {
            _db.AuthenticationIdentities.Add(new AuthenticationIdentity
            {
                UserId = userId,
                AuthenticationProviderId = provider.Id,
                Subject = identity.Subject,
                LastUsedAt = DateTimeOffset.UtcNow,
            });
            await _audit.LogEventAsync(userId, "Auth.IdentityLinked", user.Username, "Auth", user.Id, provider.Name, ct: ct);
        }
        else
        {
            existing.LastUsedAt = DateTimeOffset.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
        return user;
    }

    private async Task<AppUser?> FindOrProvisionUserAsync(AuthenticationProvider provider, ExternalIdentity identity, CancellationToken ct)
    {
        var existing = await _db.AuthenticationIdentities
            .Include(i => i.User)
            .FirstOrDefaultAsync(i => i.AuthenticationProviderId == provider.Id && i.Subject == identity.Subject, ct);
        if (existing is not null)
        {
            existing.LastUsedAt = DateTimeOffset.UtcNow;
            await _db.SaveChangesAsync(ct);
            return existing.User is { IsDisabled: false } ? existing.User : null;
        }

        var config = ExternalAuthService.ParseConfiguration(provider);
        if (config.AutoProvision == AutoProvisionPolicy.Disabled)
        {
            throw new InvalidOperationException("This provider does not allow creating new accounts. Ask an administrator to invite you first.");
        }

        var username = await GenerateUniqueUsernameAsync(identity, ct);
        var user = new AppUser
        {
            Username = username,
            NormalizedUsername = AppUser.Normalize(username),
            Email = identity.Email,
            NormalizedEmail = identity.Email is null ? null : AppUser.Normalize(identity.Email),
            DisplayName = identity.Name,
            IsAdministrator = false,
            IsDisabled = config.AutoProvision == AutoProvisionPolicy.RequireApproval,
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync(ct);

        _db.AuthenticationIdentities.Add(new AuthenticationIdentity
        {
            UserId = user.Id,
            AuthenticationProviderId = provider.Id,
            Subject = identity.Subject,
            LastUsedAt = DateTimeOffset.UtcNow,
        });
        await _audit.LogEventAsync(user.Id, "User.Create", user.Username, "User", user.Id, $"provider={provider.Name}", ct: ct);
        await _db.SaveChangesAsync(ct);

        if (user.IsDisabled)
        {
            throw new InvalidOperationException("Your account has been created but requires administrator approval before you can sign in.");
        }

        return user;
    }

    private async Task<string> GenerateUniqueUsernameAsync(ExternalIdentity identity, CancellationToken ct)
    {
        var basis = !string.IsNullOrWhiteSpace(identity.Name) ? identity.Name
            : !string.IsNullOrWhiteSpace(identity.Email) ? identity.Email.Split('@')[0]
            : identity.Subject;
        var slug = new string(basis.Trim().Select(c => char.IsLetterOrDigit(c) ? c : '-').ToArray()).Trim('-');
        if (string.IsNullOrEmpty(slug))
        {
            slug = "user";
        }

        var candidate = slug;
        var suffix = 1;
        while (await _db.Users.AnyAsync(u => u.NormalizedUsername == AppUser.Normalize(candidate), ct))
        {
            suffix++;
            candidate = $"{slug}-{suffix}";
        }
        return candidate;
    }

    private string BuildCallbackUri(string providerName)
    {
        var configuredOrigin = _config["Auth:PublicApiOrigin"];
        var origin = !string.IsNullOrWhiteSpace(configuredOrigin) ? configuredOrigin.TrimEnd('/') : $"{Request.Scheme}://{Request.Host}";
        return $"{origin}/api/auth/external/{providerName}/callback";
    }

    private string ResolveReturnOrigin(string? candidate)
    {
        var allowed = _config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (!string.IsNullOrEmpty(candidate) && allowed.Contains(candidate, StringComparer.OrdinalIgnoreCase))
        {
            return candidate;
        }

        return allowed.FirstOrDefault(o => !string.IsNullOrEmpty(o)) ?? "";
    }

    private IActionResult RedirectToFrontendError(string origin, string message) =>
        Redirect($"{origin}/auth/callback#error={Uri.EscapeDataString(message)}");
}
