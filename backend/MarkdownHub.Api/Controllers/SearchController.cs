using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

[ApiController]
[Authorize]
public class SearchController : ControllerBase
{
    private readonly SearchIndexService _search;
    private readonly PermissionService _permissions;
    private readonly CurrentUserService _currentUser;

    public SearchController(SearchIndexService search, PermissionService permissions, CurrentUserService currentUser)
    {
        _search = search;
        _permissions = permissions;
        _currentUser = currentUser;
    }

    [HttpGet("api/search")]
    public async Task<IActionResult> Search([FromQuery] string q, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(q)) return Ok(Array.Empty<object>());

        // Index-backed search (not a full-file scan), then filter to what this user may view.
        var hits = await _search.SearchAsync(q, limit: 100, ct);
        var grants = await _permissions.GetGrantsAsync(user.Id, ct);

        var visible = new List<object>();
        foreach (var hit in hits)
        {
            if (_permissions.HasAtLeast(user, grants, hit.RelativePath, PermissionLevel.View))
                visible.Add(new { hit.RelativePath, hit.PageName, hit.Snippet });
            if (visible.Count >= 50) break;
        }
        return Ok(visible);
    }
}
