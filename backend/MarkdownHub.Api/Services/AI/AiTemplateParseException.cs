namespace MarkdownHub.Api.Services;

/// <summary>Thrown when a template can't be used as an AI Template - its message is safe to show
/// the user, since the only causes are authoring mistakes in their own template.</summary>
public class AiTemplateParseException : Exception
{
    public AiTemplateParseException(string message) : base(message)
    {
    }
}
