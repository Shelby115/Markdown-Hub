using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

public record BacklinkResult(string RelativePath, string PageName);

[ApiController]
[Route("api/backlinks")]
[Authorize]
public class BacklinksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PermissionService _permissions;
    private readonly CurrentUserService _currentUser;

    public BacklinksController(AppDbContext db, PermissionService permissions, CurrentUserService currentUser)
    {
        _db = db;
        _permissions = permissions;
        _currentUser = currentUser;
    }

    [HttpGet("{**relativePath}")]
    public async Task<IActionResult> GetBacklinks(string relativePath, CancellationToken ct)
    {
        var user = await _currentUser.GetOrCreateAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, relativePath, PermissionLevel.View, ct)) return Forbid();

        var page = await _db.Pages.FirstOrDefaultAsync(p => p.RelativePath == relativePath && !p.IsDeleted, ct);
        if (page is null) return Ok(Array.Empty<BacklinkResult>());

        var incoming = await _db.PageLinks
            .Where(l => l.TargetPageId == page.Id)
            .Include(l => l.SourcePage)
            .ToListAsync(ct);
        var grants = await _permissions.GetGrantsAsync(user.Id, ct);

        var results = new List<BacklinkResult>();
        foreach (var link in incoming)
        {
            if (link.SourcePage is null) continue;
            if (_permissions.HasAtLeast(user, grants, link.SourcePage.RelativePath, PermissionLevel.View))
                results.Add(new BacklinkResult(link.SourcePage.RelativePath, link.SourcePage.PageName));
        }
        return Ok(results.DistinctBy(r => r.RelativePath));
    }
}
