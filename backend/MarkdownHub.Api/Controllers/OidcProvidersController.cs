using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

public record OidcProviderResponse(
    int Id, string Name, string Authority, string ClientId, string Audience,
    bool RequireHttpsMetadata, bool IsEnabled, DateTimeOffset CreatedAt);

public record SaveOidcProviderRequest(
    string Name, string Authority, string ClientId, string Audience, bool RequireHttpsMetadata = true);

/// <summary>
/// Admin-only CRUD for the OIDC identity providers the app accepts logins/tokens from (see
/// Services/OidcProviderValidationService.cs for how these are actually used to validate
/// incoming bearer tokens). At least one enabled provider must exist at all times - deleting or
/// disabling the last one would lock every admin out with no way back in.
/// </summary>
[ApiController]
[Route("api/admin/oidc-providers")]
[Authorize(Policy = "RequireAdministrator")]
public class OidcProvidersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly AuditLogService _audit;

    public OidcProvidersController(AppDbContext db, CurrentUserService currentUser, AuditLogService audit)
    {
        _db = db;
        _currentUser = currentUser;
        _audit = audit;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var providers = await _db.OidcProviders.OrderBy(p => p.Id).ToListAsync(ct);
        return Ok(providers.Select(ToResponse));
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SaveOidcProviderRequest request, CancellationToken ct)
    {
        var validationError = ValidateRequest(request);
        if (validationError is not null) return BadRequest(new { message = validationError });

        var provider = new OidcProvider
        {
            Name = request.Name.Trim(),
            Authority = request.Authority.Trim(),
            ClientId = request.ClientId.Trim(),
            Audience = request.Audience.Trim(),
            RequireHttpsMetadata = request.RequireHttpsMetadata,
            IsEnabled = true
        };
        _db.OidcProviders.Add(provider);
        await _db.SaveChangesAsync(ct);
        await LogAsync("OidcProvider.Create", provider, ct);
        return Ok(ToResponse(provider));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, [FromBody] SaveOidcProviderRequest request, CancellationToken ct)
    {
        var provider = await _db.OidcProviders.FindAsync([id], ct);
        if (provider is null) return NotFound();

        var validationError = ValidateRequest(request);
        if (validationError is not null) return BadRequest(new { message = validationError });

        provider.Name = request.Name.Trim();
        provider.Authority = request.Authority.Trim();
        provider.ClientId = request.ClientId.Trim();
        provider.Audience = request.Audience.Trim();
        provider.RequireHttpsMetadata = request.RequireHttpsMetadata;
        await _db.SaveChangesAsync(ct);
        await LogAsync("OidcProvider.Update", provider, ct);
        return Ok(ToResponse(provider));
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var provider = await _db.OidcProviders.FindAsync([id], ct);
        if (provider is null) return NotFound();

        if (provider.IsEnabled)
        {
            var otherEnabled = await _db.OidcProviders.CountAsync(p => p.IsEnabled && p.Id != id, ct);
            if (otherEnabled == 0) return BadRequest(new { message = "At least one enabled OIDC provider is required." });
        }

        _db.OidcProviders.Remove(provider);
        await _db.SaveChangesAsync(ct);
        await LogAsync("OidcProvider.Delete", provider, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/enable")]
    public async Task<IActionResult> Enable(int id, CancellationToken ct)
    {
        var provider = await _db.OidcProviders.FindAsync([id], ct);
        if (provider is null) return NotFound();
        provider.IsEnabled = true;
        await _db.SaveChangesAsync(ct);
        await LogAsync("OidcProvider.Enable", provider, ct);
        return Ok(ToResponse(provider));
    }

    [HttpPost("{id:int}/disable")]
    public async Task<IActionResult> Disable(int id, CancellationToken ct)
    {
        var provider = await _db.OidcProviders.FindAsync([id], ct);
        if (provider is null) return NotFound();

        var otherEnabled = await _db.OidcProviders.CountAsync(p => p.IsEnabled && p.Id != id, ct);
        if (otherEnabled == 0) return BadRequest(new { message = "At least one enabled OIDC provider is required." });

        provider.IsEnabled = false;
        await _db.SaveChangesAsync(ct);
        await LogAsync("OidcProvider.Disable", provider, ct);
        return Ok(ToResponse(provider));
    }

    private static string? ValidateRequest(SaveOidcProviderRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name)) return "Name is required.";
        if (string.IsNullOrWhiteSpace(request.Authority)
            || !Uri.TryCreate(request.Authority.Trim(), UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            return "Authority must be a valid http(s) URL.";
        if (string.IsNullOrWhiteSpace(request.ClientId)) return "Client ID is required.";
        if (string.IsNullOrWhiteSpace(request.Audience)) return "Audience is required.";
        return null;
    }

    private static OidcProviderResponse ToResponse(OidcProvider p) =>
        new(p.Id, p.Name, p.Authority, p.ClientId, p.Audience, p.RequireHttpsMetadata, p.IsEnabled, p.CreatedAt);

    private async Task LogAsync(string action, OidcProvider provider, CancellationToken ct)
    {
        var actingUser = await _currentUser.GetOrCreateAsync(ct);
        await _audit.LogEventAsync(actingUser?.Id, action, provider.Name, "OidcProvider", provider.Id,
            $"authority={provider.Authority}", ct: ct);
    }
}
