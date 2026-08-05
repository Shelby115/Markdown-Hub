using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace MarkdownHub.Api.Services;

public record PageDto(string RelativePath, string PageName, string Content, DateTimeOffset LastModifiedUtc, long SizeBytes);

public record WriteResult(PageDto Page, VersionRecordResult VersionResult);

public class ConcurrentEditConflictException : Exception
{
    public string ConflictRelativePath { get; }
    public ConcurrentEditConflictException(string conflictRelativePath)
        : base("The file changed on disk since it was opened; your edit was saved as a conflict copy.")
    {
        ConflictRelativePath = conflictRelativePath;
    }
}

/// <summary>Thrown when restoring a soft-deleted document would collide with a different, active
/// document that now occupies the same path.</summary>
public class RestorePathConflictException : Exception
{
    public RestorePathConflictException(string relativePath)
        : base($"\"{relativePath}\" is now in use by a different page - it can't be restored to that path.") { }
}

/// <summary>
/// Reads and writes Markdown files on the hub filesystem, keeping the SQLite
/// index (PageMetadata + search + backlinks) in sync on every write.
/// The filesystem is always the source of truth for content; SQLite is a cache.
///
/// PageMetadata.Id is also the stable "logical document ID" that DocumentVersion/activity
/// history is keyed on - see the class doc on PageMetadata.IsDeleted for why deletion here is
/// a soft-delete rather than removing the row, and why rename/move update RelativePath in place
/// rather than ever recreating the row.
/// </summary>
public class MarkdownFileService
{
    private readonly HubPathService _hub;
    private readonly AppDbContext _db;
    private readonly SearchIndexService _search;
    private readonly VersionService _versions;

    public MarkdownFileService(HubPathService hub, AppDbContext db, SearchIndexService search, VersionService versions)
    {
        _hub = hub;
        _db = db;
        _search = search;
        _versions = versions;
    }

    public async Task<PageDto> ReadAsync(string relativePath, CancellationToken ct = default)
    {
        var path = _hub.ResolveSafe(relativePath);
        if (!File.Exists(path)) throw new FileNotFoundException("Page not found.", relativePath);

        var content = await File.ReadAllTextAsync(path, ct);
        var info = new FileInfo(path);
        return new PageDto(relativePath, Path.GetFileNameWithoutExtension(path), content, info.LastWriteTimeUtc, info.Length);
    }

    /// <summary>
    /// Writes new content for a page. If <paramref name="expectedLastModifiedUtc"/> is
    /// supplied and doesn't match the file's current mtime, the file was changed by
    /// someone else since the caller loaded it - the incoming edit is saved as a
    /// conflict file instead of overwriting, and the original is left untouched.
    ///
    /// Every successful write is evaluated for version history (see VersionService) - most
    /// autosaves will not produce a new version, only meaningfully-different, settled content.
    /// </summary>
    public async Task<WriteResult> WriteAsync(string relativePath, string content, DateTimeOffset? expectedLastModifiedUtc,
        int actingUserId, CancellationToken ct = default)
    {
        var path = _hub.ResolveSafe(relativePath);
        var exists = File.Exists(path);

        if (exists && expectedLastModifiedUtc is not null)
        {
            var currentMtime = File.GetLastWriteTimeUtc(path);
            // Compare with 1s tolerance - filesystem mtime resolution varies by platform.
            if (Math.Abs((currentMtime - expectedLastModifiedUtc.Value.UtcDateTime).TotalSeconds) > 1)
            {
                var conflictPath = await SaveConflictCopyAsync(relativePath, content, actingUserId, ct);
                throw new ConcurrentEditConflictException(conflictPath);
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, ct);

        var meta = await IndexPageAsync(relativePath, content, ct);
        var versionResult = await _versions.MaybeRecordVersionAsync(meta.Id, relativePath, content, actingUserId, ct);

        var info = new FileInfo(path);
        var page = new PageDto(relativePath, Path.GetFileNameWithoutExtension(path), content, info.LastWriteTimeUtc, info.Length);
        return new WriteResult(page, versionResult);
    }

    /// <summary>
    /// Soft-deletes a page: removes the file from disk and hides it from the tree/search/
    /// wikilink resolution, but never touches its PageMetadata row or version history - both
    /// stay in place so an authorized user can restore it during the retention window.
    /// </summary>
    public async Task DeleteAsync(string relativePath, int actingUserId, CancellationToken ct = default)
    {
        var path = _hub.ResolveSafe(relativePath);
        if (File.Exists(path)) File.Delete(path);

        var meta = await _db.Pages.FirstOrDefaultAsync(p => p.RelativePath == relativePath && !p.IsDeleted, ct);
        if (meta is not null)
        {
            await _versions.CloseOpenVersionAsync(meta.Id, ct);
            meta.IsDeleted = true;
            meta.DeletedAtUtc = DateTimeOffset.UtcNow;
            meta.DeletedByAppUserId = actingUserId;
            await _db.SaveChangesAsync(ct);
        }
        await _search.RemoveAsync(relativePath, ct);
    }

    /// <summary>
    /// Renames/moves a page. Updates the existing PageMetadata row's RelativePath in place
    /// (preserving Id) instead of leaving it to the FileSystemWatcher's generic created/deleted
    /// handling, which would otherwise see this as an unrelated delete-then-create and destroy
    /// the document's stable ID - and with it, its version history.
    /// </summary>
    public async Task RenameAsync(string fromRelativePath, string toRelativePath, CancellationToken ct = default)
    {
        var from = _hub.ResolveSafe(fromRelativePath);
        var to = _hub.ResolveSafe(toRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        File.Move(from, to);

        var meta = await _db.Pages.FirstOrDefaultAsync(p => p.RelativePath == fromRelativePath && !p.IsDeleted, ct);
        if (meta is not null)
        {
            meta.RelativePath = toRelativePath;
            meta.PageName = Path.GetFileNameWithoutExtension(toRelativePath);
            await _db.SaveChangesAsync(ct);
        }
        await _search.RemoveAsync(fromRelativePath, ct);
        // Re-index at the new path so search/backlinks reflect it immediately rather than
        // waiting for the watcher's next debounce pass.
        await IndexPageAsync(toRelativePath, null, ct);
    }

    /// <summary>
    /// Renames/moves a folder and every document inside it (recursively) - same stable-Id
    /// preservation principle as RenameAsync, applied to every PageMetadata row (active or
    /// soft-deleted) whose path falls under the old folder, so each document keeps its version/
    /// activity history. FolderPermission grants pointing at the folder itself or anything
    /// nested inside it move with it too, so access doesn't quietly disappear on rename. Returns
    /// the number of documents whose path was updated, for the activity log.
    /// </summary>
    public async Task<int> RenameFolderAsync(string fromFolderPath, string toFolderPath, CancellationToken ct = default)
    {
        var from = _hub.ResolveSafe(fromFolderPath);
        var to = _hub.ResolveSafe(toFolderPath);
        Directory.CreateDirectory(Path.GetDirectoryName(to)!);
        Directory.Move(from, to);

        var prefix = fromFolderPath + "/";
        var affectedPages = await _db.Pages.Where(p => p.RelativePath.StartsWith(prefix)).ToListAsync(ct);
        var renames = new List<(PageMetadata Page, string OldRelativePath, string NewRelativePath)>();
        foreach (var page in affectedPages)
        {
            var oldRelativePath = page.RelativePath;
            var newRelativePath = toFolderPath + "/" + oldRelativePath[prefix.Length..];
            renames.Add((page, oldRelativePath, newRelativePath));
            page.RelativePath = newRelativePath;
        }
        await _db.SaveChangesAsync(ct);

        var affectedPermissions = await _db.FolderPermissions
            .Where(p => p.FolderPath == fromFolderPath || p.FolderPath.StartsWith(prefix))
            .ToListAsync(ct);
        foreach (var permission in affectedPermissions)
        {
            permission.FolderPath = permission.FolderPath == fromFolderPath
                ? toFolderPath
                : toFolderPath + "/" + permission.FolderPath[prefix.Length..];
        }
        if (affectedPermissions.Count > 0) await _db.SaveChangesAsync(ct);

        foreach (var (page, oldRelativePath, newRelativePath) in renames)
        {
            await _search.RemoveAsync(oldRelativePath, ct);
            // Soft-deleted documents have no file on disk (and aren't search-indexed) - only
            // re-index the ones that are still live.
            if (!page.IsDeleted) await IndexPageAsync(newRelativePath, null, ct);
        }

        return renames.Count;
    }

    /// <summary>
    /// Deletes a folder and everything inside it (recursively) from disk, soft-deleting every
    /// active document under it the same way <see cref="DeleteAsync"/> soft-deletes a single
    /// page - version history is preserved and each document can still be recovered
    /// individually during the retention window even though the folder itself is gone.
    /// Returns the number of documents soft-deleted, for the activity log.
    /// </summary>
    public async Task<int> DeleteFolderAsync(string folderPath, int actingUserId, CancellationToken ct = default)
    {
        var absolute = _hub.ResolveSafe(folderPath);

        var prefix = folderPath + "/";
        var affectedPages = await _db.Pages
            .Where(p => !p.IsDeleted && (p.RelativePath == folderPath || p.RelativePath.StartsWith(prefix)))
            .ToListAsync(ct);

        foreach (var page in affectedPages)
        {
            await _versions.CloseOpenVersionAsync(page.Id, ct);
            page.IsDeleted = true;
            page.DeletedAtUtc = DateTimeOffset.UtcNow;
            page.DeletedByAppUserId = actingUserId;
        }
        if (affectedPages.Count > 0) await _db.SaveChangesAsync(ct);

        if (Directory.Exists(absolute)) Directory.Delete(absolute, recursive: true);

        foreach (var page in affectedPages) await _search.RemoveAsync(page.RelativePath, ct);

        return affectedPages.Count;
    }

    /// <summary>
    /// Writes explicitly-restored content (from an old version, or from a soft-deleted
    /// document's last known state) to disk, for the specific PageMetadata row identified by
    /// the caller - never re-derived by path, since a soft-deleted row can't be found that way
    /// (see IndexPageAsync). Deliberately bypasses the normal version-coalescing path in
    /// WriteAsync - the caller (VersionsController, via VersionService) has already created the
    /// definitive Restore version itself, per the rule that restoring must always mint a new
    /// version rather than being subject to the usual debounce/coalesce rules.
    /// </summary>
    public async Task<PageDto> WriteRestoredContentAsync(PageMetadata meta, string content, CancellationToken ct = default)
    {
        if (meta.IsDeleted)
        {
            var collision = await _db.Pages.AnyAsync(p => p.RelativePath == meta.RelativePath && !p.IsDeleted && p.Id != meta.Id, ct);
            if (collision) throw new RestorePathConflictException(meta.RelativePath);
        }

        var path = _hub.ResolveSafe(meta.RelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, ct);

        var info = new FileInfo(path);
        meta.LastModifiedUtc = info.LastWriteTimeUtc;
        meta.SizeBytes = info.Length;
        meta.PageName = Path.GetFileNameWithoutExtension(meta.RelativePath);
        if (meta.IsDeleted)
        {
            meta.IsDeleted = false;
            meta.DeletedAtUtc = null;
            meta.DeletedByAppUserId = null;
        }
        await _db.SaveChangesAsync(ct);

        await RebuildOutgoingLinksAsync(meta, content, ct);
        var folderName = Path.GetDirectoryName(meta.RelativePath)?.Replace('\\', '/') ?? "";
        await _search.UpsertAsync(meta.RelativePath, meta.PageName, folderName, StripMarkdownSyntax(content), ct);

        return new PageDto(meta.RelativePath, meta.PageName, content, info.LastWriteTimeUtc, info.Length);
    }

    private async Task<string> SaveConflictCopyAsync(string relativePath, string content, int actingUserId, CancellationToken ct)
    {
        var timestamp = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH-mm-ssZ");
        var dir = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
        var nameNoExt = Path.GetFileNameWithoutExtension(relativePath);
        var conflictRelative = (dir.Length > 0 ? dir + "/" : "") + $"{nameNoExt}.conflict.{timestamp}.md";

        var conflictAbsolute = _hub.ResolveSafe(conflictRelative);
        await File.WriteAllTextAsync(conflictAbsolute, content, ct);

        _db.ConflictFiles.Add(new ConflictFile
        {
            OriginalRelativePath = relativePath,
            ConflictRelativePath = conflictRelative,
            CreatedByAppUserId = actingUserId
        });
        await _db.SaveChangesAsync(ct);

        return conflictRelative;
    }

    /// <summary>Updates PageMetadata, PageLinks (for backlinks), and the search index for one
    /// page, and returns the (possibly newly-created) PageMetadata row.</summary>
    public async Task<PageMetadata> IndexPageAsync(string relativePath, string? content, CancellationToken ct = default)
    {
        content ??= await File.ReadAllTextAsync(_hub.ResolveSafe(relativePath), ct);
        var pageName = Path.GetFileNameWithoutExtension(relativePath);
        var folderName = Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? "";
        var info = new FileInfo(_hub.ResolveSafe(relativePath));

        // Only ever attaches to the *active* row at this path - a soft-deleted page at the same
        // path (see PageMetadata.IsDeleted) is a distinct logical document and must not have its
        // history silently inherited by whatever gets (re)created at that path next.
        var meta = await _db.Pages.FirstOrDefaultAsync(p => p.RelativePath == relativePath && !p.IsDeleted, ct);
        if (meta is null)
        {
            meta = new PageMetadata { RelativePath = relativePath, PageName = pageName };
            _db.Pages.Add(meta);
        }
        meta.LastModifiedUtc = info.LastWriteTimeUtc;
        meta.SizeBytes = info.Length;
        await _db.SaveChangesAsync(ct);

        await RebuildOutgoingLinksAsync(meta, content, ct);

        var plainText = StripMarkdownSyntax(content);
        await _search.UpsertAsync(relativePath, pageName, folderName, plainText, ct);

        return meta;
    }

    private async Task RebuildOutgoingLinksAsync(PageMetadata meta, string content, CancellationToken ct)
    {
        var existing = await _db.PageLinks.Where(l => l.SourcePageId == meta.Id).ToListAsync(ct);
        _db.PageLinks.RemoveRange(existing);

        var parsed = WikiLinkParser.Parse(content);
        foreach (var link in parsed.DistinctBy(l => l.Target))
        {
            var target = await _db.Pages.FirstOrDefaultAsync(
                p => p.PageName == link.Target.Split('/').Last() && !p.IsDeleted, ct);

            _db.PageLinks.Add(new PageLink
            {
                SourcePageId = meta.Id,
                TargetPageId = target?.Id,
                RawTarget = link.Target
            });
        }
        await _db.SaveChangesAsync(ct);
    }

    private static string StripMarkdownSyntax(string markdown)
    {
        // Cheap plain-text projection for the search index - good enough for FTS
        // relevance without pulling in a full Markdown-to-text renderer per write.
        var noLinks = System.Text.RegularExpressions.Regex.Replace(markdown, @"\[\[([^\]]+)\]\]", "$1");
        var noSyntax = System.Text.RegularExpressions.Regex.Replace(noLinks, @"[#*`>_~\-]", " ");
        return noSyntax;
    }
}
