using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using MarkdownHub.Api.Data.Entities;
using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers;

[ApiController]
[Authorize]
public class AttachmentsController : ControllerBase
{
    private readonly HubPathService _hub;
    private readonly PermissionService _permissions;
    private readonly CurrentUserService _currentUser;
    private readonly IConfiguration _config;

    private static readonly Dictionary<string, byte[]> MagicBytes = new()
    {
        [".png"] = [0x89, 0x50, 0x4E, 0x47],
        [".jpg"] = [0xFF, 0xD8, 0xFF],
        [".jpeg"] = [0xFF, 0xD8, 0xFF],
        [".gif"] = [0x47, 0x49, 0x46, 0x38],
        [".webp"] = [0x52, 0x49, 0x46, 0x46], // "RIFF"
    };

    public AttachmentsController(HubPathService hub, PermissionService permissions, CurrentUserService currentUser, IConfiguration config)
    {
        _hub = hub;
        _permissions = permissions;
        _currentUser = currentUser;
        _config = config;
    }

    /// <summary>
    /// Uploads an image (e.g. pasted into the editor) into the hub's .attachments
    /// folder within the target folder's tree. Validates extension, size, and magic
    /// bytes so a renamed .exe can't slip through as a ".png".
    /// </summary>
    [HttpPost("api/attachments")]
    [RequestSizeLimit(50_000_000)]
    public async Task<IActionResult> Upload([FromQuery] string folder, IFormFile file, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (!await _permissions.HasAtLeastAsync(user.Id, folder, PermissionLevel.Edit, ct)) return Forbid();

        var allowedExtensions = _config.GetSection("Hub:AllowedFileExtensions").Get<string[]>()
            ?? [".png", ".jpg", ".jpeg", ".gif", ".webp"];
        var maxSize = _config.GetValue<long?>("Hub:MaximumUploadSizeBytes") ?? 20_971_520;

        var ext = Path.GetExtension(file.FileName).ToLowerInvariant();
        if (!allowedExtensions.Contains(ext))
            return BadRequest(new { message = $"File extension '{ext}' is not allowed." });
        if (file.Length == 0 || file.Length > maxSize)
            return BadRequest(new { message = "File is empty or exceeds the maximum upload size." });

        using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        var bytes = ms.ToArray();

        if (MagicBytes.TryGetValue(ext, out var expectedMagic) &&
            (bytes.Length < expectedMagic.Length || !bytes.Take(expectedMagic.Length).SequenceEqual(expectedMagic)))
        {
            return BadRequest(new { message = "File content does not match its extension." });
        }

        var safeName = $"{Guid.NewGuid():N}{ext}";
        var relativeAttachmentPath = $"{folder.Trim('/')}/.attachments/{safeName}".TrimStart('/');
        var absolutePath = _hub.ResolveSafe(relativeAttachmentPath);
        Directory.CreateDirectory(Path.GetDirectoryName(absolutePath)!);
        await System.IO.File.WriteAllBytesAsync(absolutePath, bytes, ct);

        return Ok(new { relativePath = relativeAttachmentPath, markdownSyntax = $"![{safeName}](./{safeName})" });
    }

    /// <summary>
    /// Resolves a bare filename (e.g. a wiki-style "![[Overview Map.png]]" embed target,
    /// which carries no folder) to wherever it actually lives in the hub - mirrors how other
    /// wiki-style note apps "search the whole hub by filename" for embed/link resolution.
    /// <paramref name="from"/> is the hub-relative *folder* of the page the link/embed
    /// appeared on (never a file path), used to pick the closest match when more than one
    /// file shares that filename (see HubPathService.PickClosestMatch); omit it and an
    /// ambiguous name just resolves deterministically instead of by proximity.
    /// </summary>
    [HttpGet("api/attachments/resolve")]
    public async Task<IActionResult> Resolve([FromQuery] string filename, [FromQuery] string? from, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();
        if (string.IsNullOrWhiteSpace(filename)) return NotFound();

        var relativePath = _hub.FindByFilename(filename, from);
        if (relativePath is null) return NotFound();

        var folder = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
        // Deliberately 404 (not 403) on a permission miss so this can't be used to probe for
        // the existence of files in folders the caller can't see.
        if (!await _permissions.HasAtLeastAsync(user.Id, folder, PermissionLevel.View, ct)) return NotFound();

        return Ok(new { relativePath });
    }

    [HttpGet("api/attachments/{**relativePath}")]
    public async Task<IActionResult> GetAttachment(string relativePath, CancellationToken ct)
    {
        var user = await _currentUser.GetCurrentAsync(ct);
        if (user is null) return Unauthorized();

        var folder = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
        if (!await _permissions.HasAtLeastAsync(user.Id, folder, PermissionLevel.View, ct)) return Forbid();

        try
        {
            var path = _hub.ResolveSafe(relativePath);
            if (!System.IO.File.Exists(path)) return NotFound();
            var contentType = Path.GetExtension(path).ToLowerInvariant() switch
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
                _ => "application/octet-stream"
            };
            // enableRangeProcessing lets the browser's native <audio>/<video>/PDF viewer request
            // just the bytes it needs (seeking, progressive playback) instead of requiring the
            // whole file up front - important once files get past a few MB, which images rarely
            // do but audio/video/PDF commonly are.
            return PhysicalFile(path, contentType, enableRangeProcessing: true);
        }
        catch (UnauthorizedAccessException)
        {
            return Forbid();
        }
    }
}
