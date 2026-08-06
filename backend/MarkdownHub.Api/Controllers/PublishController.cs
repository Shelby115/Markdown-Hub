using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

public record PublishRequest(bool Published);

[ApiController]
public class PublishController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly PermissionService _permissions;
    private readonly CurrentUserService _currentUser;
    private readonly MarkdownFileService _files;
    private readonly MarkdownRenderService _renderer;
    private readonly HubPathService _hub;

    public PublishController(AppDbContext db, PermissionService permissions, CurrentUserService currentUser,
        MarkdownFileService files, MarkdownRenderService renderer, HubPathService hub)
    {
        _db = db;
        _permissions = permissions;
        _currentUser = currentUser;
        _files = files;
        _renderer = renderer;
        _hub = hub;
    }

    /// <summary>Toggle publish state. Requires Edit access on the page. Auth required.</summary>
    [Authorize]
    [HttpPost("api/publish/{**relativePath}")]
    public async Task<IActionResult> SetPublished(string relativePath, [FromBody] PublishRequest request, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, relativePath, PermissionLevel.Edit, ct)) return Forbid();

        var meta = await _db.Pages.FirstOrDefaultAsync(p => p.RelativePath == relativePath && !p.IsDeleted, ct);
        if (meta is null) return NotFound();

        meta.IsPublished = request.Published;
        // Unpublishing immediately invalidates the slug so the old URL 404s right away;
        // re-publishing later mints a fresh slug rather than reusing a possibly-guessed one.
        meta.PublishSlug = request.Published
            ? (meta.PublishSlug ?? Guid.NewGuid().ToString("N")[..12])
            : null;

        await _db.SaveChangesAsync(ct);
        return Ok(new { meta.IsPublished, meta.PublishSlug });
    }

    /// <summary>
    /// Public, unauthenticated read of a published page by its slug. Does not expose
    /// the underlying hub file path, and does not grant access to any files the
    /// page references unless those are separately published.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("api/publish/view/{slug}")]
    public async Task<IActionResult> ViewPublished(string slug, CancellationToken ct)
    {
        var meta = await _db.Pages.FirstOrDefaultAsync(p => p.PublishSlug == slug && p.IsPublished && !p.IsDeleted, ct);
        if (meta is null) return NotFound();

        var page = await _files.ReadAsync(meta.RelativePath, ct);
        var html = _renderer.RenderToSafeHtml(
            page.Content,
            target => ResolvePublicLink(target, ct),
            target => ResolvePublicEmbedSrc(target, slug));
        return Ok(new { meta.PageName, Html = html });
    }

    private (string Href, bool Exists) ResolvePublicLink(string target, CancellationToken ct)
    {
        // Only ever resolves to another public slug URL - never leaks the filesystem path,
        // and only reports "exists" if that target is ALSO published.
        var candidate = target.EndsWith(".md", StringComparison.OrdinalIgnoreCase) ? target : target + ".md";
        var meta = _db.Pages.FirstOrDefault(p => (p.RelativePath == candidate || p.PageName == target) && !p.IsDeleted);
        if (meta is { IsPublished: true, PublishSlug: not null })
            return ($"/published/{meta.PublishSlug}", true);
        return ("#", false);
    }

    private static string? ResolvePublicEmbedSrc(string target, string slug) =>
        $"/api/publish/{Uri.EscapeDataString(slug)}/attachment?filename={Uri.EscapeDataString(target)}";

    /// <summary>
    /// Anonymous, publish-scoped file fetch for a published page's embeds (images, audio,
    /// video, PDFs). Only works while the referencing page is currently published, and only
    /// ever serves files with a recognized embeddable extension - this can't be used to fetch
    /// arbitrary hub content (e.g. other .md files) by guessing filenames.
    /// </summary>
    [AllowAnonymous]
    [HttpGet("api/publish/{slug}/attachment")]
    public async Task<IActionResult> GetPublishedAttachment(string slug, [FromQuery] string filename, CancellationToken ct)
    {
        var publishedPage = await _db.Pages.FirstOrDefaultAsync(p => p.PublishSlug == slug && p.IsPublished && !p.IsDeleted, ct);
        if (publishedPage is null || string.IsNullOrWhiteSpace(filename)) return NotFound();

        var ext = Path.GetExtension(filename).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".gif" => "image/gif",
            ".webp" => "image/webp",
            ".svg" => "image/svg+xml",
            ".mp3" => "audio/mpeg",
            ".wav" => "audio/wav",
            ".ogg" => "audio/ogg",
            ".m4a" => "audio/mp4",
            ".flac" => "audio/flac",
            ".aac" => "audio/aac",
            ".mp4" => "video/mp4",
            ".webm" => "video/webm",
            ".ogv" => "video/ogg",
            ".mov" => "video/quicktime",
            ".mkv" => "video/x-matroska",
            ".pdf" => "application/pdf",
            _ => null
        };
        if (contentType is null) return NotFound();

        var currentFolder = Path.GetDirectoryName(publishedPage.RelativePath)?.Replace('\\', '/') ?? "";
        var relativePath = _hub.FindByFilename(filename, currentFolder);
        if (relativePath is null) return NotFound();

        var absolute = _hub.ResolveSafe(relativePath);
        if (!System.IO.File.Exists(absolute)) return NotFound();

        return PhysicalFile(absolute, contentType, enableRangeProcessing: true);
    }
}
