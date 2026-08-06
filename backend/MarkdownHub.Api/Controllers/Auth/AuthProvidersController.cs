using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;

namespace MarkdownHub.Api.Controllers;

public record AuthProviderResponse(int Id, string Name, string Authority, string ClientId);

/// <summary>
/// Public (pre-login) list of enabled OIDC providers, so the SPA can show a "sign in with..."
/// screen - or, in the common single-provider case, redirect straight there - before it has any
/// token. Deliberately excludes Audience/RequireHttpsMetadata: those are resource-server-only
/// validation details the frontend never needs.
/// </summary>
[ApiController]
[Route("api/auth/providers")]
[AllowAnonymous]
public class AuthProvidersController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuthProvidersController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var providers = await _db.OidcProviders
            .Where(p => p.IsEnabled)
            .OrderBy(p => p.Id)
            .Select(p => new AuthProviderResponse(p.Id, p.Name, p.Authority, p.ClientId))
            .ToListAsync(ct);
        return Ok(providers);
    }
}
