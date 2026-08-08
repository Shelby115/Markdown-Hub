using MarkdownHub.Api.Services;

namespace MarkdownHub.Api.Controllers.AI;

public record AiTemplateParseRequest(string TemplatePath);

/// <summary>One ordered piece of the template as the client sees it: literal Markdown to keep
/// verbatim, or the id of a slot to fill. Exactly one is set.</summary>
public record AiTemplateElementDto(string? Text, string? SlotId);

public record AiTemplateSlotDto(string Id, string Name, int Index, int Count);

public record AiTemplateParseResponse(
    List<AiTemplateElementDto> Elements,
    List<AiTemplateSlotDto> Slots,
    List<string> FillInVariables);

public record AiTemplateGenerateRequest(string TemplatePath, string SlotId, string Mode, List<AiTemplateSlotValue> Slots);

public record AiTemplateGenerateResponse(string Content, List<string> Warnings);
