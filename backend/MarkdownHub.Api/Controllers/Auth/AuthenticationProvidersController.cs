using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities.Auth;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.Auth;

public record AuthenticationProviderResponse(
    int Id, string Name, string DisplayName, AuthProviderType Type, string ClientId,
    bool HasClientSecret, ProviderConfiguration Configuration, bool Enabled,
    DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, int UsersUsingProvider);

public record CreateAuthenticationProviderRequest(
    string Name, string DisplayName, AuthProviderType Type, string ClientId,
    string? ClientSecret, ProviderConfiguration Configuration);

public record UpdateAuthenticationProviderRequest(
    string DisplayName, AuthProviderType Type, string ClientId,
    string? ClientSecret, ProviderConfiguration Configuration);

public record ProviderPresetResponse(string Key, string DisplayName, AuthProviderType Type, ProviderConfiguration Configuration);

/// <summary>
/// Admin-only CRUD for external authentication providers (Auth.md §27). Unlike the old
/// OIDC-provider model, at least one enabled provider is never required - local username/password
/// always works, so the only safety guard here is "don't strand the last administrator" (see
/// AccountSafetyService), not "keep at least one provider enabled."
/// </summary>
[ApiController]
[Route("api/admin/auth-providers")]
[Authorize(Policy = "RequireAdministrator")]
public class AuthenticationProvidersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly AuditLogService _audit;
    private readonly AccountSafetyService _safety;
    private readonly ProviderSecretProtector _secretProtector;

    public AuthenticationProvidersController(AppDbContext db, CurrentUserService currentUser, AuditLogService audit,
        AccountSafetyService safety, ProviderSecretProtector secretProtector)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
        _safety = safety;
        _secretProtector = secretProtector;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var providers = await _db.AuthenticationProviders.OrderBy(p => p.Id).ToListAsync(ct);
        var responses = new List<AuthenticationProviderResponse>();
        foreach (var p in providers) responses.Add(await ToResponseAsync(p, ct));
        return Ok(responses);
    }

    [HttpGet("presets")]
    public IActionResult Presets() =>
        Ok(ProviderPresets.All.Select(p => new ProviderPresetResponse(p.Key, p.DisplayName, p.Type, p.Configuration)));

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateAuthenticationProviderRequest request, CancellationToken ct)
    {
        var validationError = ValidateRequest(request.DisplayName, request.ClientId, request.Type, request.Configuration);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var name = ProviderNameSlug.Create(request.Name, "");
        if (string.IsNullOrEmpty(name)) return BadRequest(new { message = "Name is required." });
        if (await _db.AuthenticationProviders.AnyAsync(p => p.Name == name, ct))
            return Conflict(new { message = "A provider with that name already exists." });

        var provider = new AuthenticationProvider
        {
            Name = name,
            DisplayName = request.DisplayName.Trim(),
            Type = request.Type,
            ClientId = request.ClientId.Trim(),
            ClientSecretProtected = string.IsNullOrEmpty(request.ClientSecret) ? null : _secretProtector.Protect(request.ClientSecret),
            ConfigurationJson = JsonSerializer.Serialize(request.Configuration),
            Enabled = true,
        };
        _db.AuthenticationProviders.Add(provider);
        await _db.SaveChangesAsync(ct);
        await LogAsync("Auth.ProviderCreated", provider, ct);
        return Ok(await ToResponseAsync(provider, ct));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateAuthenticationProviderRequest request, CancellationToken ct)
    {
        var provider = await _db.AuthenticationProviders.FindAsync([id], ct);
        if (provider is null) return NotFound();

        var validationError = ValidateRequest(request.DisplayName, request.ClientId, request.Type, request.Configuration);
        if (validationError is not null) return BadRequest(new { message = validationError });

        provider.DisplayName = request.DisplayName.Trim();
        provider.Type = request.Type;
        provider.ClientId = request.ClientId.Trim();
        if (!string.IsNullOrEmpty(request.ClientSecret))
            provider.ClientSecretProtected = _secretProtector.Protect(request.ClientSecret);
        provider.ConfigurationJson = JsonSerializer.Serialize(request.Configuration);
        provider.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await LogAsync("Auth.ProviderModified", provider, ct);
        return Ok(await ToResponseAsync(provider, ct));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var provider = await _db.AuthenticationProviders.FindAsync([id], ct);
        if (provider is null) return NotFound();

        if (await _safety.WouldProviderRemovalStrandLastAdministratorAsync(id, ct))
            return BadRequest(new { message = "Removing this provider would leave the last administrator with no way to sign in." });

        _db.AuthenticationProviders.Remove(provider);
        await _db.SaveChangesAsync(ct);
        await LogAsync("Auth.ProviderDeleted", provider, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/enable")]
    public async Task<IActionResult> Enable(int id, CancellationToken ct)
    {
        var provider = await _db.AuthenticationProviders.FindAsync([id], ct);
        if (provider is null) return NotFound();
        provider.Enabled = true;
        provider.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await LogAsync("Auth.ProviderEnabled", provider, ct);
        return Ok(await ToResponseAsync(provider, ct));
    }

    [HttpPost("{id:int}/disable")]
    public async Task<IActionResult> Disable(int id, CancellationToken ct)
    {
        var provider = await _db.AuthenticationProviders.FindAsync([id], ct);
        if (provider is null) return NotFound();

        if (await _safety.WouldProviderRemovalStrandLastAdministratorAsync(id, ct))
            return BadRequest(new { message = "Disabling this provider would leave the last administrator with no way to sign in." });

        provider.Enabled = false;
        provider.UpdatedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(ct);
        await LogAsync("Auth.ProviderDisabled", provider, ct);
        return Ok(await ToResponseAsync(provider, ct));
    }

    private static string? ValidateRequest(string displayName, string clientId, AuthProviderType type, ProviderConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(displayName)) return "Display name is required.";
        if (string.IsNullOrWhiteSpace(clientId)) return "Client ID is required.";
        if (type == AuthProviderType.Oidc && string.IsNullOrWhiteSpace(configuration.Authority))
            return "Authority is required for an OIDC provider.";
        if (type == AuthProviderType.OAuth2 &&
            (string.IsNullOrWhiteSpace(configuration.AuthorizationEndpoint) || string.IsNullOrWhiteSpace(configuration.TokenEndpoint)))
            return "Authorization and token endpoints are required for an OAuth 2.0 provider.";
        return null;
    }

    private async Task<AuthenticationProviderResponse> ToResponseAsync(AuthenticationProvider p, CancellationToken ct)
    {
        var config = ExternalAuthService.ParseConfiguration(p);
        var usersUsing = await _safety.CountUsersUsingProviderAsync(p.Id, ct);
        return new AuthenticationProviderResponse(p.Id, p.Name, p.DisplayName, p.Type, p.ClientId,
            p.ClientSecretProtected is not null, config, p.Enabled, p.CreatedAt, p.UpdatedAt, usersUsing);
    }

    private async Task LogAsync(string action, AuthenticationProvider provider, CancellationToken ct)
    {
        var actingUser = await _currentUser.GetCurrentAsync(ct);
        await _audit.LogEventAsync(actingUser?.Id, action, provider.DisplayName, "AuthenticationProvider", provider.Id,
            $"type={provider.Type}", ct: ct);
    }
}
