namespace MarkdownHub.Api.Controllers.AI;

public record AiEditRequest(string Action, string Text);
public record AiEditResponse(string Result);
