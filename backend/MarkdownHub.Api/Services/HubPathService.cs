namespace MarkdownHub.Api.Services;

/// <summary>
/// Every filesystem path that touches user input MUST go through this service.
/// It resolves a relative hub path to an absolute path and guarantees the
/// result stays inside the configured MarkdownRoot, blocking path traversal
/// (../, absolute paths, symlink escapes, encoded separators, etc).
/// </summary>
public class HubPathService
{
    private readonly string _root;

    public HubPathService(IConfiguration config)
    {
        var configuredRoot = config["Hub:MarkdownRoot"]
            ?? throw new InvalidOperationException("Hub:MarkdownRoot is not configured");
        _root = Path.GetFullPath(configuredRoot);
        Directory.CreateDirectory(_root);
    }

    public string Root => _root;

    /// <summary>
    /// Resolves a user-supplied relative path (e.g. "Projects/Ideas.md") to a safe
    /// absolute path. Throws UnauthorizedAccessException if the result would escape
    /// the hub root.
    /// </summary>
    public string ResolveSafe(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return _root;

        // Reject obviously hostile input before touching the filesystem APIs.
        if (relativePath.Contains('\0'))
            throw new UnauthorizedAccessException("Invalid path.");

        // Normalize separators, strip any leading separators / drive-letter attempts.
        var normalized = relativePath.Replace('\\', '/').TrimStart('/');

        var combined = Path.GetFullPath(Path.Combine(_root, normalized));

        // The resolved path must be the root itself or nested under root + separator.
        var rootWithSep = _root.EndsWith(Path.DirectorySeparatorChar)
            ? _root
            : _root + Path.DirectorySeparatorChar;

        if (!combined.Equals(_root, StringComparison.Ordinal) &&
            !combined.StartsWith(rootWithSep, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException("Path traversal outside the hub is not allowed.");
        }

        return combined;
    }

    /// <summary>Converts an absolute path back to a hub-relative path using '/' separators.</summary>
    public string ToRelative(string absolutePath)
    {
        var rel = Path.GetRelativePath(_root, absolutePath);
        return rel.Replace(Path.DirectorySeparatorChar, '/');
    }

    /// <summary>
    /// Searches the whole hub for every file matching the given filename (case-insensitive),
    /// skipping hidden folders (.attachments, and any other dot-prefixed app/tool config
    /// folder), and returns whichever single one is the right match. Mirrors how other
    /// wiki-style note apps resolve a link/embed target that doesn't specify a folder - by
    /// filename, hub-wide, not by exact relative path. With more than one file sharing that
    /// filename (common in any hub big enough to have e.g. the same NPC name reused across
    /// campaigns), <paramref name="relativeToFolder"/> - the folder of the page the link/embed
    /// appeared on - picks the closest one rather than an arbitrary one; see
    /// <see cref="PickClosestMatch"/>. Returns null if nothing matches.
    /// </summary>
    public string? FindByFilename(string filename, string? relativeToFolder = null)
    {
        var matches = Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories)
            .Where(f => !ToRelative(f).Split('/').Any(segment => segment.StartsWith('.')))
            .Where(f => string.Equals(Path.GetFileName(f), filename, StringComparison.OrdinalIgnoreCase))
            .Select(ToRelative)
            .ToList();
        return PickClosestMatch(matches, relativeToFolder);
    }

    /// <summary>
    /// Picks the best of several same-named candidates. Kept as a pure function, separate
    /// from the directory walk in <see cref="FindByFilename"/>, so the decision logic can be
    /// unit tested against a hand-built candidate list instead of a real directory tree. Among
    /// multiple candidates, picks whichever shares the longest folder path with
    /// <paramref name="relativeToFolder"/> (e.g. "Angryria/Campaigns/Campaign 1/Sessions", never
    /// a file path) - the number of directory levels you'd have to walk up from it and back down
    /// to reach the candidate ("tree distance") - so a same-folder or nearby file wins over a
    /// same-named file elsewhere in the hub. Ties break on shorter overall path, then
    /// alphabetically, so the result is always deterministic - never dependent on filesystem
    /// enumeration order, which is what made this flaky before.
    /// </summary>
    public static string? PickClosestMatch(IReadOnlyList<string> candidates, string? relativeToFolder)
    {
        if (candidates.Count == 0) return null;
        if (candidates.Count == 1) return candidates[0];

        var fromSegments = SplitFolder(relativeToFolder);
        return candidates
            .OrderBy(c => TreeDistance(fromSegments, SplitFolder(FolderOf(c))))
            .ThenBy(c => c.Length)
            .ThenBy(c => c, StringComparer.OrdinalIgnoreCase)
            .First();
    }

    /// <summary>The folder portion of a hub-relative file path, e.g. "Angryria/Encounters/Side
    /// Adventures/Evil Fairy.md" -> "Angryria/Encounters/Side Adventures".</summary>
    private static string FolderOf(string relativeFilePath)
    {
        var normalized = relativeFilePath.Replace('\\', '/');
        var lastSlash = normalized.LastIndexOf('/');
        return lastSlash < 0 ? "" : normalized[..lastSlash];
    }

    private static string[] SplitFolder(string? folderPath) =>
        string.IsNullOrEmpty(folderPath)
            ? []
            : folderPath.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);

    private static int TreeDistance(string[] a, string[] b)
    {
        var common = 0;
        while (common < a.Length && common < b.Length &&
               string.Equals(a[common], b[common], StringComparison.OrdinalIgnoreCase))
        {
            common++;
        }
        return (a.Length - common) + (b.Length - common);
    }

    public bool IsMarkdownFile(string relativePath) =>
        relativePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
}
