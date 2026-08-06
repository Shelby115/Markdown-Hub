namespace MarkdownHub.Api.Data.Entities;

/// <summary>Why a version exists - purely informational, doesn't affect storage/restoration.</summary>
public static class DocumentVersionType
{
    public const string Edit = "Edit";
    public const string Restore = "Restore";
}
