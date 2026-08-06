namespace MarkdownHub.Api.Services;

/// <summary>Thrown when restoring a soft-deleted document would collide with a different, active
/// document that now occupies the same path.</summary>
public class RestorePathConflictException : Exception
{
    public RestorePathConflictException(string relativePath)
        : base($"\"{relativePath}\" is now in use by a different page - it can't be restored to that path.") { }
}
