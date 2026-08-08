namespace MarkdownHub.Api.Services;

/// <summary>Whether a slot is being generated from scratch or revised in place.</summary>
public enum AiTemplateMode
{
    Generate,
    Improve,
}

/// <summary>One placeholder occurrence. Id is "Name#Index" (1-based); Count is how many times
/// that name appears in the template, so a prompt can say "item 3 of 4".</summary>
public record AiTemplateSlot(string Id, string Name, int Index, int Count);

/// <summary>The authored rules for one placeholder name. Rules holds free-text bullets passed
/// to the model verbatim; the rest are the recognized typed prefixes.</summary>
public record AiTemplateInstruction(string Name, List<string> Rules, string? Format, string? Example, int? MaxWords, int? MaxSentences);

/// <summary>One ordered piece of the template: either literal Markdown or a slot, never both.</summary>
public record AiTemplateElement(string? LiteralText, AiTemplateSlot? Slot);

public record ParsedAiTemplate(
    List<AiTemplateElement> Elements,
    Dictionary<string, AiTemplateInstruction> Instructions,
    List<string> FillInVariables,
    string Purpose)
{
    public List<AiTemplateSlot> Slots => Elements.Where(e => e.Slot is not null).Select(e => e.Slot!).ToList();
}

/// <summary>A slot's current state as the client knows it - sent back on every generate call so
/// the backend can stay stateless and still build context-aware prompts.</summary>
public record AiTemplateSlotValue(string Id, string Content, bool Locked);

public record AiTemplateValidation(bool IsValid, List<string> Problems);

public record AiTemplateSlotResult(string Content, List<string> Warnings);
