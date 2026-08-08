namespace MarkdownHub.Api.Services;

/// <summary>
/// Generates one AI Template slot: prompt, clean, validate, and at most one correction retry.
/// Never throws on validation failure - a result that still fails after the retry comes back with
/// warnings attached, because a failed check must never blank out content the user might want.
///
/// A slot whose instructions name a generation pool is served from that pool instead, which is
/// near-instant; only an empty pool falls through to a live model call.
/// </summary>
public class AiTemplateService
{
    private readonly IAiService _ai;
    private readonly GenerationPoolService _pools;

    public AiTemplateService(IAiService ai, GenerationPoolService pools)
    {
        _ai = ai;
        _pools = pools;
    }

    public async Task<AiTemplateSlotResult> GenerateSlotAsync(
        ParsedAiTemplate template,
        AiTemplateSlot slot,
        IReadOnlyList<AiTemplateSlotValue> slotValues,
        AiTemplateMode mode,
        CancellationToken ct = default)
    {
        var instruction = template.Instructions.GetValueOrDefault(slot.Name);

        // Improve rewrites text the user already has, so there's nothing a pool could contribute.
        if (mode == AiTemplateMode.Generate && instruction?.Pool is string poolName)
        {
            return await FromPoolAsync(poolName, instruction, ct);
        }

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

    /// <summary>Takes a ready entry from the pool, or generates one live if the pool is empty and
    /// records it as used so the background generator won't produce the same thing again.</summary>
    private async Task<AiTemplateSlotResult> FromPoolAsync(string poolName, AiTemplateInstruction instruction, CancellationToken ct)
    {
        var entry = await _pools.TakeAsync(poolName, ct);
        if (entry is not null)
        {
            return new AiTemplateSlotResult(entry.Content, [], entry.Id);
        }

        // The pool's own rules are what a pooled entry would have been written from, so a live
        // fallback uses them too - the user gets the same kind of content either way.
        var poolInstruction = await PoolInstructionAsync(poolName, instruction, ct);
        var prompt = AiTemplatePromptBuilder.BuildForPool(poolInstruction, [], null);
        var content = AiTemplateValidator.Clean(await _ai.CompleteAsync(AiPrompts.AiTemplateSystemPrompt, prompt, ct));
        var validation = AiTemplateValidator.Check(content, poolInstruction);

        await _pools.RecordUsedAsync(poolName, content, ct);
        return new AiTemplateSlotResult(content, validation.Problems);
    }

    /// <summary>The pool's rules, or the template's own as a fallback when the named pool doesn't
    /// exist - a typo'd or deleted pool name degrades to ordinary generation rather than failing.</summary>
    private async Task<AiTemplateInstruction> PoolInstructionAsync(string poolName, AiTemplateInstruction instruction, CancellationToken ct)
    {
        var pool = await _pools.FindPoolAsync(poolName, ct);
        return pool is null
            ? instruction
            : AiTemplateParser.ParseInstruction(pool.Name, pool.Instructions);
    }
}
