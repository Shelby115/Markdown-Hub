using Microsoft.EntityFrameworkCore;
using MarkdownHub.Api.Data;
using MarkdownHub.Api.Data.Entities;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Resolves what level of access a user has to a given hub-relative path.
/// Permissions are granted per-folder and inherited by sub-folders/files unless a
/// more specific (longer path prefix) grant exists for that user.
/// </summary>
public class PermissionService
{
    private readonly AppDbContext _db;
    private readonly HubPathService _hub;

    public PermissionService(AppDbContext db, HubPathService hub)
    {
        _db = db;
        _hub = hub;
    }

    public async Task<PermissionLevel?> GetEffectiveLevelAsync(int appUserId, string relativePath, CancellationToken ct = default)
    {
        var user = await _db.Users.FindAsync([appUserId], ct);
        if (user is null || user.IsDisabled) return null;
        if (user.IsAdministrator) return PermissionLevel.Manage;

        var grants = await _db.FolderPermissions
            .Where(p => p.AppUserId == appUserId)
            .ToListAsync(ct);
        return GetEffectiveLevel(user, grants, relativePath);
    }

    public async Task<bool> HasAtLeastAsync(int appUserId, string relativePath, PermissionLevel required, CancellationToken ct = default)
    {
        var level = await GetEffectiveLevelAsync(appUserId, relativePath, ct);
        return level is not null && level >= required;
    }

    /// <summary>
    /// Fetches this user's grants once, for callers that need to check many paths (e.g. building
    /// the whole file tree) without re-querying FolderPermissions on every single item - pair
    /// with the synchronous GetEffectiveLevel/HasAtLeast overloads below.
    /// </summary>
    public async Task<IReadOnlyList<FolderPermission>> GetGrantsAsync(int appUserId, CancellationToken ct = default) =>
        await _db.FolderPermissions.Where(p => p.AppUserId == appUserId).ToListAsync(ct);

    public PermissionLevel? GetEffectiveLevel(AppUser user, IReadOnlyList<FolderPermission> grants, string relativePath)
    {
        if (user.IsDisabled) return null;
        if (user.IsAdministrator) return PermissionLevel.Manage;

        var folderPath = CanonicalizeToFolder(relativePath);
        if (folderPath is null) return null; // path would escape the hub root entirely - deny

        // Find the most specific (longest) matching folder prefix.
        FolderPermission? best = null;
        foreach (var grant in grants)
        {
            if (IsPrefixMatch(grant.FolderPath, folderPath))
            {
                if (best is null || grant.FolderPath.Length > best.FolderPath.Length)
                    best = grant;
            }
        }
        return best?.Level;
    }

    public bool HasAtLeast(AppUser user, IReadOnlyList<FolderPermission> grants, string relativePath, PermissionLevel required) =>
        GetEffectiveLevel(user, grants, relativePath) is { } level && level >= required;

    private static bool IsPrefixMatch(string grantFolder, string targetFolder)
    {
        if (grantFolder.Length == 0) return true; // root grant applies everywhere
        return targetFolder.Equals(grantFolder, StringComparison.OrdinalIgnoreCase)
            || targetFolder.StartsWith(grantFolder + "/", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves relativePath through the exact same canonicalization the filesystem layer uses
    /// (HubPathService.ResolveSafe collapses "../" and "." segments via Path.GetFullPath)
    /// before extracting its containing folder. Without this, a permission check operating on
    /// the raw un-canonicalized string could disagree with which folder a path actually resolves
    /// to on disk - e.g. "Public/../Private/secret.md" naively prefix-matches a grant on
    /// "Public", while the file layer resolves it straight into "Private". Returns null (a deny)
    /// if the path would escape the hub root entirely.
    /// </summary>
    private string? CanonicalizeToFolder(string relativePath)
    {
        string canonicalRelative;
        try
        {
            canonicalRelative = _hub.ToRelative(_hub.ResolveSafe(relativePath));
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        return NormalizeToFolder(canonicalRelative);
    }

    private static string NormalizeToFolder(string relativePath)
    {
        var folder = relativePath.Contains('.') && !relativePath.EndsWith('/')
            ? Path.GetDirectoryName(relativePath)?.Replace('\\', '/') ?? ""
            : relativePath.TrimEnd('/');
        return folder;
    }
}
