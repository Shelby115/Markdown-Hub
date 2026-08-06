namespace MarkdownHub.Api.Services;

public class AiServiceException : Exception
{
    public AiServiceException(string message, Exception? inner = null) : base(message, inner) { }
}
