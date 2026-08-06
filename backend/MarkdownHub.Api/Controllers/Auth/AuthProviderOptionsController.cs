using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities.Auth;

namespace MarkdownHub.Api.Controllers.Auth;

/// <summary>
/// Public (pre-login) list of enabled external providers, so the SPA can show "sign in with..."
/// buttons alongside the always-available local login form. Never required to be non-empty -
/// local username/password works with zero providers configured (Auth.md §5).
/// </summary>
[ApiController]
[AllowAnonymous]
public class AuthProviderOptionsController : ControllerBase
{
    private readonly AppDbContext _db;

    public AuthProviderOptionsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet("api/auth/providers")]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var providers = await _db.AuthenticationProviders
            .Where(p => p.Enabled)
            .OrderBy(p => p.Id)
            .Select(p => new AuthProviderResponse(p.Id, p.Name, p.DisplayName, p.Type))
            .ToListAsync(ct);
        return Ok(providers);
    }
}
