using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

public record VersionSummaryDto(int Id, int DocumentId, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    bool IsOpen, string VersionType, int? UserId, string? Username, string RelativePath);

public record VersionDetailDto(int Id, int DocumentId, DateTimeOffset CreatedAtUtc, DateTimeOffset UpdatedAtUtc,
    bool IsOpen, string VersionType, int? UserId, string? Username, string RelativePath, string Content);

public record DocumentHistoryDto(int DocumentId, string RelativePath, bool IsDeleted, IReadOnlyList<VersionSummaryDto> Versions);

public record CompareResultDto(VersionDetailDto From, VersionDetailDto To);

public record DeletedDocumentDto(int DocumentId, string RelativePath, string PageName,
    DateTimeOffset? DeletedAtUtc, int? DeletedByUserId, string? DeletedByUsername, int? LatestVersionId);

/// <summary>
/// Version History API - see Activity-And-History.md. Every endpoint enforces the same
/// folder-permission model as normal document access (View to read/compare, Edit to restore a
/// version of a live document, Manage to restore a deleted one - matching the level DeletePage
/// itself requires), never relying on the frontend to hide anything.
/// </summary>
[ApiController]
[Authorize]
public class VersionsController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PermissionService _permissions;
    private readonly CurrentUserService _currentUser;
    private readonly VersionService _versions;
    private readonly MarkdownFileService _files;
    private readonly AuditLogService _audit;

    public VersionsController(AppDbContext db, PermissionService permissions, CurrentUserService currentUser,
        VersionService versions, MarkdownFileService files, AuditLogService audit)
    {
        _db = db;
        _permissions = permissions;
        _currentUser = currentUser;
        _versions = versions;
        _files = files;
        _audit = audit;
    }

    /// <summary>History for the currently-active document at this path.</summary>
    [HttpGet("api/versions/by-path/{**relativePath}")]
    public async Task<IActionResult> GetHistoryByPath(string relativePath, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, relativePath, PermissionLevel.View, ct)) return Forbid();

        var meta = await _db.Pages.FirstOrDefaultAsync(p => p.RelativePath == relativePath && !p.IsDeleted, ct);
        if (meta is null) return NotFound();

        return Ok(await BuildHistoryDtoAsync(meta, ct));
    }

    [HttpGet("api/versions/{versionId:int}")]
    public async Task<IActionResult> GetVersion(int versionId, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        var version = await _versions.GetVersionAsync(versionId, ct);
        if (version is null) return NotFound();

        var meta = await _db.Pages.FirstOrDefaultAsync(p => p.Id == version.DocumentId, ct);
        if (meta is null) return NotFound();
        if (!await _permissions.HasAtLeastAsync(user.Id, meta.RelativePath, PermissionLevel.View, ct)) return Forbid();

        return Ok(await ToDetailDtoAsync(version, ct));
    }

    [HttpGet("api/versions/compare")]
    public async Task<IActionResult> Compare([FromQuery] int fromId, [FromQuery] int toId, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        var from = await _versions.GetVersionAsync(fromId, ct);
        var to = await _versions.GetVersionAsync(toId, ct);
        if (from is null || to is null) return NotFound();
        if (from.DocumentId != to.DocumentId) return BadRequest(new { message = "Both versions must belong to the same document." });

        var meta = await _db.Pages.FirstOrDefaultAsync(p => p.Id == from.DocumentId, ct);
        if (meta is null) return NotFound();
        if (!await _permissions.HasAtLeastAsync(user.Id, meta.RelativePath, PermissionLevel.View, ct)) return Forbid();

        return Ok(new CompareResultDto(await ToDetailDtoAsync(from, ct), await ToDetailDtoAsync(to, ct)));
    }

    /// <summary>
    /// Restores a version as the document's new current state. Works for a live document (Edit
    /// permission, same as a normal save) or a soft-deleted one (Manage permission, matching
    /// what deleting it required) - restoring an old version of an already-deleted document
    /// un-deletes it in the same step. Never overwrites/removes any existing version; always
    /// mints a new one (see VersionService.CreateRestoreVersionAsync).
    /// </summary>
    [HttpPost("api/versions/{versionId:int}/restore")]
    public async Task<IActionResult> Restore(int versionId, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        var version = await _versions.GetVersionAsync(versionId, ct);
        if (version is null) return NotFound();

        var meta = await _db.Pages.FirstOrDefaultAsync(p => p.Id == version.DocumentId, ct);
        if (meta is null) return NotFound();

        var requiredLevel = meta.IsDeleted ? PermissionLevel.Manage : PermissionLevel.Edit;
        if (!await _permissions.HasAtLeastAsync(user.Id, meta.RelativePath, requiredLevel, ct)) return Forbid();

        try
        {
            var restored = await _versions.CreateRestoreVersionAsync(meta.Id, meta.RelativePath, version.Content, user.Id, ct);
            await _files.WriteRestoredContentAsync(meta, version.Content, ct);

            await _audit.LogEventAsync(user.Id, "File.Restore", meta.RelativePath, "Document", meta.Id,
                details: $"Restored version {version.Id} (from {version.CreatedAtUtc:u})", relatedVersionId: restored.Id, ct: ct);

            return Ok(await ToDetailDtoAsync(restored, ct));
        }
        catch (RestorePathConflictException ex)
        {
            return Conflict(new { message = ex.Message });
        }
    }

    /// <summary>Soft-deleted documents the current user has at least Manage access to - the
    /// same permission level deleting them required in the first place.</summary>
    [HttpGet("api/versions/deleted")]
    public async Task<IActionResult> ListDeleted(CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        var grants = await _permissions.GetGrantsAsync(user.Id, ct);
        var deleted = await _db.Pages.Where(p => p.IsDeleted).ToListAsync(ct);
        var userIds = deleted.Where(p => p.DeletedByAppUserId != null).Select(p => p.DeletedByAppUserId!.Value).Distinct().ToList();
        var usernames = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        var accessible = deleted.Where(p => _permissions.HasAtLeast(user, grants, p.RelativePath, PermissionLevel.Manage)).ToList();
        var documentIds = accessible.Select(p => p.Id).ToList();
        var latestVersionIds = await _db.DocumentVersions
            .Where(v => documentIds.Contains(v.DocumentId))
            .GroupBy(v => v.DocumentId)
            .Select(g => new { DocumentId = g.Key, LatestId = g.Max(v => v.Id) })
            .ToDictionaryAsync(x => x.DocumentId, x => x.LatestId, ct);

        var results = accessible
            .OrderByDescending(p => p.DeletedAtUtc)
            .Select(p => new DeletedDocumentDto(
                p.Id, p.RelativePath, p.PageName, p.DeletedAtUtc, p.DeletedByAppUserId,
                p.DeletedByAppUserId is { } id && usernames.TryGetValue(id, out var name) ? name : null,
                latestVersionIds.TryGetValue(p.Id, out var latestId) ? latestId : null));

        return Ok(results);
    }

    private async Task<DocumentHistoryDto> BuildHistoryDtoAsync(PageMetadata meta, CancellationToken ct)
    {
        var versions = await _versions.GetHistoryAsync(meta.Id, ct);
        var userIds = versions.Where(v => v.UserId != null).Select(v => v.UserId!.Value).Distinct().ToList();
        var usernames = await _db.Users.Where(u => userIds.Contains(u.Id)).ToDictionaryAsync(u => u.Id, u => u.Username, ct);

        var summaries = versions.Select(v => new VersionSummaryDto(
            v.Id, v.DocumentId, v.CreatedAtUtc, v.UpdatedAtUtc, v.IsOpen, v.VersionType, v.UserId,
            v.UserId is { } id && usernames.TryGetValue(id, out var name) ? name : null, v.RelativePath
        )).ToList();

        return new DocumentHistoryDto(meta.Id, meta.RelativePath, meta.IsDeleted, summaries);
    }

    private async Task<VersionDetailDto> ToDetailDtoAsync(DocumentVersion v, CancellationToken ct)
    {
        var username = v.UserId is { } id ? (await _db.Users.FindAsync([id], ct))?.Username : null;
        return new VersionDetailDto(v.Id, v.DocumentId, v.CreatedAtUtc, v.UpdatedAtUtc, v.IsOpen, v.VersionType, v.UserId, username, v.RelativePath, v.Content);
    }
}
