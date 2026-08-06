namespace MarkdownHub.Api.Services;

public class ConcurrentEditConflictException : Exception
{
    public string ConflictRelativePath { get; }
    public ConcurrentEditConflictException(string conflictRelativePath)
        : base("The file changed on disk since it was opened; your edit was saved as a conflict copy.")
    {
        ConflictRelativePath = conflictRelativePath;
    }
}
