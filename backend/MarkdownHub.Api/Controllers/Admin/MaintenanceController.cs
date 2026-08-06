using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.Admin;

[ApiController]
[Authorize(Policy = "RequireAdministrator")]
public class MaintenanceController : ControllerBase
{
    private readonly SearchIndexService _search;
    private readonly HubPathService _hub;
    private readonly AppDbContext _db;
    private readonly BackupService _backup;

    public MaintenanceController(SearchIndexService search, HubPathService hub, AppDbContext db, BackupService backup)
    {
        _search = search;
        _hub = hub;
        _db = db;
        _backup = backup;
    }

    /// <summary>Rebuilds the FTS search index from the filesystem. SQLite metadata loss never touches Markdown content.</summary>
    [HttpPost("api/admin/maintenance/rebuild-search-index")]
    public async Task<IActionResult> RebuildSearchIndex(CancellationToken ct)
    {
        await _search.RebuildFromFilesystemAsync(_hub, ct);
        return Ok(new { message = "Search index rebuilt." });
    }

    /// <summary>Rebuilds PageMetadata + PageLinks (backlinks graph) from the filesystem, independent of the search index.</summary>
    [HttpPost("api/admin/maintenance/rebuild-metadata")]
    public async Task<IActionResult> RebuildMetadata([FromServices] MarkdownFileService fileService, CancellationToken ct)
    {
        foreach (var file in Directory.EnumerateFiles(_hub.Root, "*.md", SearchOption.AllDirectories))
        {
            var relative = _hub.ToRelative(file);
            await fileService.IndexPageAsync(relative, null, ct);
        }
        return Ok(new { message = "File metadata rebuilt." });
    }

    [HttpGet("api/admin/maintenance/conflicts")]
    public async Task<IActionResult> ListConflicts(CancellationToken ct)
    {
        var conflicts = await _db.ConflictFiles.Where(c => !c.Resolved).ToListAsync(ct);
        return Ok(conflicts);
    }

    [HttpPost("api/admin/maintenance/conflicts/{id:int}/resolve")]
    public async Task<IActionResult> ResolveConflict(int id, [FromQuery] bool deleteConflictFile, CancellationToken ct)
    {
        var conflict = await _db.ConflictFiles.FindAsync([id], ct);
        if (conflict is null) return NotFound();

        if (deleteConflictFile)
        {
            var path = _hub.ResolveSafe(conflict.ConflictRelativePath);
            if (System.IO.File.Exists(path)) System.IO.File.Delete(path);
        }
        conflict.Resolved = true;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpPost("api/admin/maintenance/backup")]
    public async Task<IActionResult> RunBackupNow(CancellationToken ct)
    {
        var record = await _backup.RunBackupAsync(manual: true, ct);
        return Ok(record);
    }

    [HttpGet("api/admin/maintenance/backups")]
    public async Task<IActionResult> ListBackups(CancellationToken ct)
    {
        var backups = await _db.Backups.OrderByDescending(b => b.CreatedAt).ToListAsync(ct);
        return Ok(backups);
    }
}
