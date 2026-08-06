using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

public record FileTreeNode(string Name, string RelativePath, bool IsFolder, List<FileTreeNode>? Children);
public record SavePageRequest(string Content, DateTimeOffset? ExpectedLastModifiedUtc);
public record RenameRequest(string NewRelativePath);
public record TemplateInfo(string RelativePath, string PageName);

[ApiController]
[Route("api/files")]
[Authorize]
public class FilesController : ControllerBase
{
    private readonly MarkdownFileService _files;
    private readonly HubPathService _hub;
    private readonly PermissionService _permissions;
    private readonly CurrentUserService _currentUser;
    private readonly MarkdownRenderService _renderer;
    private readonly AppDbContext _db;
    private readonly AuditLogService _audit;

    public FilesController(MarkdownFileService files, HubPathService hub, PermissionService permissions,
        CurrentUserService currentUser, MarkdownRenderService renderer, AppDbContext db, AuditLogService audit)
    {
        _files = files;
        _hub = hub;
        _permissions = permissions;
        _currentUser = currentUser;
        _renderer = renderer;
        _db = db;
        _audit = audit;
    }

    /// <summary>Returns the full folder/file tree the current user has at least View access to.</summary>
    [HttpGet("tree")]
    public async Task<IActionResult> GetTree(CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        // Fetch this user's grants once rather than re-querying FolderPermissions for every
        // single file/folder in the tree (BuildTree recurses over the whole hub).
        var grants = await _permissions.GetGrantsAsync(user.Id, ct);
        var root = BuildTree(_hub.Root, "", user, grants);
        return Ok(root.Children ?? []);
    }

    /// <summary>Lists every page marked as a template - these can live anywhere in the hub.</summary>
    [HttpGet("templates")]
    public async Task<IActionResult> GetTemplates(CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        var grants = await _permissions.GetGrantsAsync(user.Id, ct);
        var candidates = await _db.Pages.Where(p => p.IsTemplate && !p.IsDeleted).ToListAsync(ct);
        var results = candidates
            .Where(page => _permissions.HasAtLeast(user, grants, page.RelativePath, PermissionLevel.View))
            .Select(page => new TemplateInfo(page.RelativePath, page.PageName))
            .OrderBy(r => r.PageName);
        return Ok(results);
    }

    public record MarkTemplateRequest(bool IsTemplate);

    /// <summary>Marks/unmarks a page as available as a template when creating new pages.</summary>
    [HttpPost("mark-template/{**relativePath}")]
    public async Task<IActionResult> MarkTemplate(string relativePath, [FromBody] MarkTemplateRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, relativePath, PermissionLevel.Edit, ct)) return Forbid();

        var meta = await _db.Pages.FirstOrDefaultAsync(p => p.RelativePath == relativePath && !p.IsDeleted, ct);
        if (meta is null) return NotFound();

        meta.IsTemplate = request.IsTemplate;
        await _db.SaveChangesAsync(ct);
        return Ok(new { meta.IsTemplate });
    }

    /// <summary>Creates an empty folder in the hub.</summary>
    [HttpPost("folder/{**relativePath}")]
    public async Task<IActionResult> CreateFolder(string relativePath, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, relativePath, PermissionLevel.Edit, ct)) return Forbid();

        string absolute;
        try
        {
            absolute = _hub.ResolveSafe(relativePath);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        if (Directory.Exists(absolute) || System.IO.File.Exists(absolute))
            return Conflict(new { message = "A file or folder with that name already exists." });

        Directory.CreateDirectory(absolute);
        await _audit.LogEventAsync(user.Id, "Folder.Create", relativePath, "Folder", null, ct: ct);
        return NoContent();
    }

    private FileTreeNode BuildTree(string absoluteDir, string relativeDir, AppUser user, IReadOnlyList<FolderPermission> grants)
    {
        var children = new List<FileTreeNode>();

        foreach (var dir in Directory.EnumerateDirectories(absoluteDir).OrderBy(d => d))
        {
            if (Path.GetFileName(dir).StartsWith('.')) continue; // skip .attachments, .git, etc.
            var relPath = _hub.ToRelative(dir);
            if (!_permissions.HasAtLeast(user, grants, relPath, PermissionLevel.View)) continue;
            children.Add(BuildTree(dir, relPath, user, grants));
        }

        foreach (var file in Directory.EnumerateFiles(absoluteDir, "*.md").OrderBy(f => f))
        {
            var relPath = _hub.ToRelative(file);
            if (!_permissions.HasAtLeast(user, grants, relPath, PermissionLevel.View)) continue;
            children.Add(new FileTreeNode(Path.GetFileNameWithoutExtension(file), relPath, false, null));
        }

        return new FileTreeNode(Path.GetFileName(absoluteDir), relativeDir, true, children);
    }

    /// <summary>Loads a page fresh from disk (never cached) so external edits are always picked up.</summary>
    [HttpGet("{**relativePath}")]
    public async Task<IActionResult> GetPage(string relativePath, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, relativePath, PermissionLevel.View, ct)) return Forbid();

        try
        {
            var page = await _files.ReadAsync(relativePath, ct);
            var currentFolder = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
            var html = _renderer.RenderToSafeHtml(
                page.Content,
                target => ResolveWikiLinkHref(target, currentFolder),
                target => ResolveEmbedSrc(target, currentFolder));
            var meta = await _db.Pages.FirstOrDefaultAsync(p => p.RelativePath == relativePath && !p.IsDeleted, ct);
            return Ok(new
            {
                page.RelativePath,
                page.PageName,
                page.Content,
                Html = html,
                page.LastModifiedUtc,
                page.SizeBytes,
                IsPublished = meta?.IsPublished ?? false,
                PublishSlug = meta?.PublishSlug,
                IsTemplate = meta?.IsTemplate ?? false,
            });
        }
        catch (FileNotFoundException)
        {
            return NotFound();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpPut("{**relativePath}")]
    public async Task<IActionResult> SavePage(string relativePath, [FromBody] SavePageRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, relativePath, PermissionLevel.Edit, ct)) return Forbid();

        try
        {
            var result = await _files.WriteAsync(relativePath, request.Content, request.ExpectedLastModifiedUtc, user.Id, ct);
            var saved = result.Page;

            if (result.VersionResult.Changed)
            {
                var action = result.VersionResult.IsNewDocument ? "File.Create" : "File.Modify";
                await _audit.LogEventAsync(user.Id, action, relativePath, "Document", result.VersionResult.Version?.DocumentId,
                    relatedVersionId: result.VersionResult.Version?.Id, ct: ct);
            }

            var meta = await _db.Pages.FirstOrDefaultAsync(p => p.RelativePath == relativePath && !p.IsDeleted, ct);
            return Ok(new
            {
                saved.RelativePath,
                saved.PageName,
                saved.Content,
                saved.LastModifiedUtc,
                saved.SizeBytes,
                IsPublished = meta?.IsPublished ?? false,
                PublishSlug = meta?.PublishSlug,
                IsTemplate = meta?.IsTemplate ?? false,
            });
        }
        catch (ConcurrentEditConflictException ex)
        {
            return Conflict(new { message = ex.Message, conflictRelativePath = ex.ConflictRelativePath });
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    [HttpDelete("{**relativePath}")]
    public async Task<IActionResult> DeletePage(string relativePath, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, relativePath, PermissionLevel.Manage, ct)) return Forbid();

        var meta = await _db.Pages.FirstOrDefaultAsync(p => p.RelativePath == relativePath && !p.IsDeleted, ct);
        await _files.DeleteAsync(relativePath, user.Id, ct);
        await _audit.LogEventAsync(user.Id, "File.Delete", relativePath, "Document", meta?.Id, ct: ct);
        return NoContent();
    }

    [HttpPost("rename/{**relativePath}")]
    public async Task<IActionResult> Rename(string relativePath, [FromBody] RenameRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, relativePath, PermissionLevel.Manage, ct)) return Forbid();
        if (!await _permissions.HasAtLeastAsync(user.Id, request.NewRelativePath, PermissionLevel.Edit, ct)) return Forbid();

        try
        {
            var meta = await _db.Pages.FirstOrDefaultAsync(p => p.RelativePath == relativePath && !p.IsDeleted, ct);
            await _files.RenameAsync(relativePath, request.NewRelativePath, ct);

            var oldFolder = relativePath.Contains('/') ? relativePath[..relativePath.LastIndexOf('/')] : "";
            var newFolder = request.NewRelativePath.Contains('/') ? request.NewRelativePath[..request.NewRelativePath.LastIndexOf('/')] : "";
            var action = oldFolder == newFolder ? "File.Rename" : "File.Move";
            await _audit.LogEventAsync(user.Id, action, $"{relativePath} → {request.NewRelativePath}", "Document", meta?.Id, ct: ct);
            return NoContent();
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }

    /// <summary>Renames/moves a folder and everything inside it. Requires Manage on the source
    /// folder (same bar as deleting) and Edit on the destination (same bar as file rename).</summary>
    [HttpPost("rename-folder/{**relativePath}")]
    public async Task<IActionResult> RenameFolder(string relativePath, [FromBody] RenameRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, relativePath, PermissionLevel.Manage, ct)) return Forbid();
        if (!await _permissions.HasAtLeastAsync(user.Id, request.NewRelativePath, PermissionLevel.Edit, ct)) return Forbid();

        var newPath = request.NewRelativePath.Trim('/');
        if (string.IsNullOrWhiteSpace(newPath)) return BadRequest(new { message = "A folder name is required." });
        if (newPath == relativePath || newPath.StartsWith(relativePath + "/", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = "A folder can't be moved into itself." });

        string fromAbsolute, toAbsolute;
        try
        {
            fromAbsolute = _hub.ResolveSafe(relativePath);
            toAbsolute = _hub.ResolveSafe(newPath);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        if (!Directory.Exists(fromAbsolute)) return NotFound();
        if (Directory.Exists(toAbsolute) || System.IO.File.Exists(toAbsolute))
            return Conflict(new { message = "A file or folder with that name already exists." });

        var affected = await _files.RenameFolderAsync(relativePath, newPath, ct);

        var oldParent = relativePath.Contains('/') ? relativePath[..relativePath.LastIndexOf('/')] : "";
        var newParent = newPath.Contains('/') ? newPath[..newPath.LastIndexOf('/')] : "";
        var action = oldParent == newParent ? "Folder.Rename" : "Folder.Move";
        await _audit.LogEventAsync(user.Id, action, $"{relativePath} → {newPath}", "Folder", null,
            details: $"{affected} document(s) affected", ct: ct);

        return NoContent();
    }

    /// <summary>Deletes a folder and everything inside it. Requires Manage on the folder, same
    /// bar as deleting a single file.</summary>
    [HttpDelete("folder/{**relativePath}")]
    public async Task<IActionResult> DeleteFolder(string relativePath, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, relativePath, PermissionLevel.Manage, ct)) return Forbid();

        string absolute;
        try
        {
            absolute = _hub.ResolveSafe(relativePath);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }

        if (!Directory.Exists(absolute)) return NotFound();

        var affected = await _files.DeleteFolderAsync(relativePath, user.Id, ct);
        await _audit.LogEventAsync(user.Id, "Folder.Delete", relativePath, "Folder", null,
            details: $"{affected} document(s) affected", ct: ct);

        return NoContent();
    }

    /// <summary>
    /// Resolves an unqualified wiki-link target (e.g. "PageName" or "Folder/PageName") to a
    /// predictable app URL. A path-qualified target (containing "/") is used as-is; a bare
    /// name is searched for hub-wide, the same way other wiki-style note apps resolve a link
    /// regardless of which folder it's actually in - and the same way the live editor's client-side
    /// resolution already works via /api/attachments/resolve. <paramref name="currentFolder"/>
    /// (the folder of the page this link appears on) picks the closest match if more than one
    /// file shares that filename. Existence is checked purely by filename match here;
    /// permission filtering of the *result* happens at render/click time.
    /// </summary>
    private (string Href, bool Exists) ResolveWikiLinkHref(string target, string currentFolder)
    {
        var candidate = target.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? target : target + ".md";
        string? relativePath = candidate.Contains('/')
            ? (System.IO.File.Exists(_hub.ResolveSafe(candidate)) ? candidate : null)
            : _hub.FindByFilename(candidate, currentFolder);

        if (relativePath is null) return ($"/page/{Uri.EscapeDataString(target)}", false);

        var withoutExtension = relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            ? relativePath[..^3]
            : relativePath;
        return ($"/page/{string.Join('/', withoutExtension.Split('/').Select(Uri.EscapeDataString))}", true);
    }

    /// <summary>
    /// Resolves an image/audio/video/PDF embed target to a URL the browser can load it from.
    /// This HTML is only ever shown nested inside another page (the live editor's note-
    /// transclusion widget, liveMarkdown.ts's NoteEmbedWidget) - not the current page itself,
    /// which renders its own embeds via that same editor's dedicated media widgets instead of
    /// this endpoint's HTML. The resulting <audio>/<video>/<img> src still needs to work
    /// without an Authorization header (media elements can't send one), so - same as the live
    /// editor's own media widgets - the current request's own bearer token rides along as a
    /// query param, accepted only on the /api/attachments route (see Program.cs's
    /// OnMessageReceived). Permission is enforced by AttachmentsController itself when that
    /// URL is actually requested, not here.
    /// </summary>
    private string? ResolveEmbedSrc(string target, string currentFolder)
    {
        var relativePath = target.Contains('/')
            ? (System.IO.File.Exists(_hub.ResolveSafe(target)) ? target : null)
            : _hub.FindByFilename(target, currentFolder);
        if (relativePath is null) return null;

        var path = $"/api/attachments/{string.Join('/', relativePath.Split('/').Select(Uri.EscapeDataString))}";
        var authHeader = Request.Headers.Authorization.ToString();
        var token = authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? authHeader["Bearer ".Length..]
            : null;
        return string.IsNullOrEmpty(token) ? path : $"{path}?access_token={Uri.EscapeDataString(token)}";
    }
}
