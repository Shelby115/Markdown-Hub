namespace MarkdownHub.Api.Services;

/// <summary>
/// Generates one AI Template slot: prompt, clean, validate, and at most one correction retry.
/// Never throws on validation failure - a result that still fails after the retry comes back with
/// warnings attached, because a failed check must never blank out content the user might want.
/// </summary>
public class AiTemplateService
{
    private readonly IAiService _ai;

    public AiTemplateService(IAiService ai)
    {
        _ai = ai;
    }

    public async Task<AiTemplateSlotResult> GenerateSlotAsync(
        ParsedAiTemplate template,
        AiTemplateSlot slot,
        IReadOnlyList<AiTemplateSlotValue> slotValues,
        AiTemplateMode mode,
        CancellationToken ct = default)
    {
        var instruction = template.Instructions.GetValueOrDefault(slot.Name);

        var prompt = AiTemplatePromptBuilder.Build(template, slot, slotValues, mode, null);
        var content = AiTemplateValidator.Clean(await _ai.CompleteAsync(AiPrompts.AiTemplateSystemPrompt, prompt, ct));
        var validation = AiTemplateValidator.Check(content, instruction);
        if (validation.IsValid)
        {
            return new AiTemplateSlotResult(content, []);
        }

        var retryPrompt = AiTemplatePromptBuilder.Build(template, slot, slotValues, mode, validation.Problems);
        var retryContent = AiTemplateValidator.Clean(await _ai.CompleteAsync(AiPrompts.AiTemplateSystemPrompt, retryPrompt, ct));
        var retryValidation = AiTemplateValidator.Check(retryContent, instruction);
        if (retryValidation.IsValid)
        {
            return new AiTemplateSlotResult(retryContent, []);
        }

        // Both attempts fell short. Hand back whichever one actually has content, flagged, so the
        // user can keep it, edit it, or reroll rather than being left with nothing.
        return string.IsNullOrWhiteSpace(retryContent)
            ? new AiTemplateSlotResult(content, validation.Problems)
            : new AiTemplateSlotResult(retryContent, retryValidation.Problems);
    }
}
