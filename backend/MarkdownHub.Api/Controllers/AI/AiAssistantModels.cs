namespace MarkdownHub.Api.Controllers.AI;

public enum AssistantAction
{
    Ask,
    Summarize,
    ExpandTopic,
}

public record AssistantRequest(string Action, string? Question, List<string> ContextPaths);
public record AssistantResultCard(string Title, string Content);
public record AssistantResponse(List<AssistantResultCard> Results);
