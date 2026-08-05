using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Services;

/// <summary>Outcome of evaluating a save against the version-coalescing rules.</summary>
public record VersionRecordResult(DocumentVersion? Version, bool Changed, bool IsNewDocument);

/// <summary>
/// Owns DocumentVersion bookkeeping: the coalescing/debounce algorithm that decides whether a
/// save produces a new version, and explicit restore. Never touches the filesystem - that stays
/// MarkdownFileService's job; this only ever reads/writes the DocumentVersions table.
///
/// Coalescing algorithm (see Activity-And-History.md section 1.1): while content keeps
/// differing from whatever was there before, repeated saves update a single *open* version in
/// place instead of each minting a new row - so a short editing burst produces at most one new
/// version, not one per autosave. A save that exactly matches the open version's current content
/// (nothing changed since the last save) is a pure no-op. A save that lands back on the last
/// *closed* version's content - the state before this burst began - means the burst's net effect
/// was zero, so the open version is discarded entirely rather than left behind as a version
/// identical to its own predecessor. An open version becomes a permanent, immutable part of
/// history once no further save touches it for CoalesceWindow.
/// </summary>
public class VersionService
{
    private static readonly TimeSpan CoalesceWindow = TimeSpan.FromMinutes(10);

    private readonly AppDbContext _db;

    public VersionService(AppDbContext db)
    {
        _db = db;
    }

    public async Task<VersionRecordResult> MaybeRecordVersionAsync(
        int documentId, string relativePath, string newContent, int? userId, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var latestClosed = await _db.DocumentVersions
            .Where(v => v.DocumentId == documentId && !v.IsOpen)
            .OrderByDescending(v => v.Id)
            .FirstOrDefaultAsync(ct);
        var openVersion = await _db.DocumentVersions
            .Where(v => v.DocumentId == documentId && v.IsOpen)
            .FirstOrDefaultAsync(ct); // at most one open version per document, by construction

        var isNewDocument = latestClosed is null && openVersion is null;

        // A stale open version (nothing has touched it for a full coalescing window) is no
        // longer "in progress" - close it out as a permanent history point before evaluating
        // this save, establishing it as the new settled baseline.
        if (openVersion is not null && now - openVersion.UpdatedAtUtc > CoalesceWindow)
        {
            openVersion.IsOpen = false;
            await _db.SaveChangesAsync(ct);
            latestClosed = openVersion;
            openVersion = null;
        }

        // Nothing changed since whatever is currently "live" - the open version's content if
        // one exists (it holds the most recent save), otherwise the last closed version. This
        // is the literal "compare against current persisted content" no-op check.
        var currentContent = openVersion?.Content ?? latestClosed?.Content;
        if (currentContent is not null && newContent == currentContent)
        {
            return new VersionRecordResult(null, Changed: false, isNewDocument);
        }

        if (openVersion is not null)
        {
            // Still within the coalescing window - keep updating the same open version instead
            // of minting a new row for every save in this burst. If this particular save lands
            // exactly back on the pre-burst (closed) baseline, the whole burst's net effect is
            // zero - discard the now-pointless open version rather than leaving behind a version
            // identical to the one right before it.
            if (latestClosed is not null && newContent == latestClosed.Content)
            {
                _db.DocumentVersions.Remove(openVersion);
                await _db.SaveChangesAsync(ct);
                return new VersionRecordResult(null, Changed: false, isNewDocument);
            }

            openVersion.Content = newContent;
            openVersion.RelativePath = relativePath;
            openVersion.UpdatedAtUtc = now;
            openVersion.UserId = userId;
            await _db.SaveChangesAsync(ct);
            return new VersionRecordResult(openVersion, Changed: true, isNewDocument);
        }

        // Fresh divergence from the settled baseline (or this document's very first-ever save,
        // even with empty content - there's no real "previous state" to compare against then).
        var version = new DocumentVersion
        {
            DocumentId = documentId,
            UserId = userId,
            Content = newContent,
            RelativePath = relativePath,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            IsOpen = true,
            VersionType = DocumentVersionType.Edit,
        };
        _db.DocumentVersions.Add(version);
        await _db.SaveChangesAsync(ct);
        return new VersionRecordResult(version, Changed: true, isNewDocument);
    }

    /// <summary>Closes any open version for a document without changing its content - used when
    /// a document is deleted, so its last edit burst is frozen as final history rather than left
    /// mutable (or discardable by the coalescing rules) after the document is gone.</summary>
    public async Task CloseOpenVersionAsync(int documentId, CancellationToken ct = default)
    {
        var open = await _db.DocumentVersions.FirstOrDefaultAsync(v => v.DocumentId == documentId && v.IsOpen, ct);
        if (open is not null)
        {
            open.IsOpen = false;
            await _db.SaveChangesAsync(ct);
        }
    }

    public async Task<IReadOnlyList<DocumentVersion>> GetHistoryAsync(int documentId, CancellationToken ct = default) =>
        await _db.DocumentVersions
            .Where(v => v.DocumentId == documentId)
            .OrderByDescending(v => v.Id)
            .ToListAsync(ct);

    public Task<DocumentVersion?> GetVersionAsync(int versionId, CancellationToken ct = default) =>
        _db.DocumentVersions.FirstOrDefaultAsync(v => v.Id == versionId, ct);

    /// <summary>
    /// Restoring never overwrites or removes any existing version - it always mints a brand new,
    /// immediately-closed version carrying the restored content, becoming the new current state.
    /// Any in-progress open version is closed out first rather than silently overwritten.
    /// </summary>
    public async Task<DocumentVersion> CreateRestoreVersionAsync(
        int documentId, string relativePath, string content, int? userId, CancellationToken ct = default)
    {
        await CloseOpenVersionAsync(documentId, ct);

        var restored = new DocumentVersion
        {
            DocumentId = documentId,
            UserId = userId,
            Content = content,
            RelativePath = relativePath,
            IsOpen = false,
            VersionType = DocumentVersionType.Restore,
        };
        _db.DocumentVersions.Add(restored);
        await _db.SaveChangesAsync(ct);
        return restored;
    }

    /// <summary>Permanently removes versions older than <paramref name="retentionDays"/> across
    /// every document. Safe to run repeatedly; never touches the current document state (only
    /// this table).</summary>
    public async Task<int> CleanupExpiredVersionsAsync(int retentionDays, CancellationToken ct = default)
    {
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        // EF Core's SQLite provider can't translate a DateTimeOffset column comparison into SQL
        // (SQLite has no native DateTimeOffset type) - filter client-side instead. This table is
        // already retention-bounded, so loading it in full for a periodic cleanup pass is fine.
        var all = await _db.DocumentVersions.ToListAsync(ct);
        var stale = all.Where(v => v.CreatedAtUtc < cutoff).ToList();
        if (stale.Count == 0) return 0;
        _db.DocumentVersions.RemoveRange(stale);
        await _db.SaveChangesAsync(ct);
        return stale.Count;
    }
}
