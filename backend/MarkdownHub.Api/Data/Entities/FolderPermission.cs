namespace MarkdownHub.Api.Data.Entities;

public enum PermissionLevel
{
    View = 0,
    Edit = 1,
    Manage = 2 // rename/delete
}

/// <summary>
/// Grants a user a permission level on a folder (relative path from MarkdownRoot).
/// Applies recursively to sub-folders unless overridden by a more specific entry.
/// </summary>
public class FolderPermission
{
    public int Id { get; set; }
    public int AppUserId { get; set; }
    public AppUser? AppUser { get; set; }

    /// <summary>Relative folder path from the hub root. "" (empty) = hub root.</summary>
    public required string FolderPath { get; set; }
    public PermissionLevel Level { get; set; }
}
