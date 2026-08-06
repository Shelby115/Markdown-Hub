using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

/// <summary>Powers the "[[ " autocomplete suggestion dropdown in the editor.</summary>
[ApiController]
[Authorize]
public class WikiLinkSuggestionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PermissionService _permissions;
    private readonly CurrentUserService _currentUser;

    public WikiLinkSuggestionsController(AppDbContext db, PermissionService permissions, CurrentUserService currentUser)
    {
        _db = db;
        _permissions = permissions;
        _currentUser = currentUser;
    }

    [HttpGet("api/wikilink-suggestions")]
    public async Task<IActionResult> Suggest([FromQuery] string prefix, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        prefix ??= "";
        var candidates = await _db.Pages
            .Where(p => p.PageName.StartsWith(prefix) && !p.IsDeleted)
            .OrderBy(p => p.PageName)
            .Take(200) // permission filter below may drop some; overfetch a bit
            .ToListAsync(ct);
        var grants = await _permissions.GetGrantsAsync(user.Id, ct);

        var results = new List<object>();
        foreach (var page in candidates)
        {
            if (_permissions.HasAtLeast(user, grants, page.RelativePath, PermissionLevel.View))
                results.Add(new { page.RelativePath, page.PageName });
            if (results.Count >= 20) break;
        }
        return Ok(results);
    }
}
