using System.Text;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Builds the user prompt for one slot as explicitly labeled blocks, so rules, format examples,
/// and already-generated context can never be mistaken for each other by the model.
/// </summary>
public static class AiTemplatePromptBuilder
{
    private const int MaxContextCharsPerSlot = 1000;

    public static string Build(
        ParsedAiTemplate template,
        AiTemplateSlot slot,
        IReadOnlyList<AiTemplateSlotValue> slotValues,
        AiTemplateMode mode,
        IReadOnlyList<string>? problems)
    {
        var instruction = template.Instructions.GetValueOrDefault(slot.Name);
        var prompt = new StringBuilder();

        if (template.Purpose.Length > 0)
        {
            prompt.AppendLine("TEMPLATE PURPOSE");
            prompt.AppendLine(template.Purpose);
            prompt.AppendLine();
        }

        prompt.AppendLine("SECTION TO GENERATE");
        prompt.AppendLine($"Name: {slot.Name}");
        if (slot.Count > 1)
        {
            prompt.AppendLine($"This is item {slot.Index} of {slot.Count} for this section. It must be clearly different from the others.");
        }
        if (instruction is not null)
        {
            foreach (var rule in instruction.Rules)
            {
                prompt.AppendLine($"- {rule}");
            }
            if (instruction.Format is not null)
            {
                prompt.AppendLine($"- Required format: {instruction.Format}");
            }
            if (instruction.MaxWords is not null)
            {
                prompt.AppendLine($"- Hard limit: {instruction.MaxWords} words.");
            }
            if (instruction.MaxSentences is not null)
            {
                prompt.AppendLine($"- Hard limit: {instruction.MaxSentences} sentence(s).");
            }
        }
        prompt.AppendLine();

        // The example is fenced with its own warning rather than relying on the system prompt
        // alone: in a long context the system prompt is far away, and copying the example's
        // subject matter is the single most common failure mode for this kind of generation.
        if (instruction?.Example is string example)
        {
            prompt.AppendLine("FORMAT EXAMPLE (style and shape only - never reuse its subject matter, wording, or ideas)");
            prompt.AppendLine(example);
            prompt.AppendLine("END OF FORMAT EXAMPLE");
            prompt.AppendLine();
        }

        var context = BuildContext(template, slot, slotValues);
        if (context.Length > 0)
        {
            prompt.AppendLine("ALREADY GENERATED (the rest of this document - stay consistent with it and don't repeat it)");
            prompt.Append(context);
            prompt.AppendLine();
        }

        if (mode == AiTemplateMode.Improve)
        {
            var current = slotValues.FirstOrDefault(v => v.Id == slot.Id)?.Content ?? "";
            prompt.AppendLine("CURRENT TEXT");
            prompt.AppendLine(current);
            prompt.AppendLine();
            prompt.AppendLine("Task: rewrite the current text so it reads better and follows the rules more closely. Keep the same subject and concept.");
        }
        else
        {
            prompt.AppendLine($"Task: write the content for {slot.Name}. Reply with only that content.");
        }

        if (problems is not null && problems.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("PROBLEM WITH YOUR LAST REPLY");
            foreach (var problem in problems)
            {
                prompt.AppendLine($"- {problem}");
            }
            prompt.AppendLine("Write the content again, corrected.");
        }

        return prompt.ToString();
    }

    /// <summary>Builds the prompt for a generation pool entry. There is no document context here -
    /// a pool entry is written to stand on its own - so variety comes from showing the model what
    /// the pool already holds and telling it not to repeat any of it.</summary>
    public static string BuildForPool(AiTemplateInstruction instruction, IReadOnlyList<string> existingEntries, IReadOnlyList<string>? problems)
    {
        var prompt = new StringBuilder();

        prompt.AppendLine("SECTION TO GENERATE");
        prompt.AppendLine($"Name: {instruction.Name}");
        foreach (var rule in instruction.Rules)
        {
            prompt.AppendLine($"- {rule}");
        }
        if (instruction.Format is not null)
        {
            prompt.AppendLine($"- Required format: {instruction.Format}");
        }
        if (instruction.MaxWords is not null)
        {
            prompt.AppendLine($"- Hard limit: {instruction.MaxWords} words.");
        }
        if (instruction.MaxSentences is not null)
        {
            prompt.AppendLine($"- Hard limit: {instruction.MaxSentences} sentence(s).");
        }
        prompt.AppendLine();

        if (instruction.Example is string example)
        {
            prompt.AppendLine("FORMAT EXAMPLE (style and shape only - never reuse its subject matter, wording, or ideas)");
            prompt.AppendLine(example);
            prompt.AppendLine("END OF FORMAT EXAMPLE");
            prompt.AppendLine();
        }

        if (existingEntries.Count > 0)
        {
            prompt.AppendLine("ALREADY WRITTEN (write something clearly different from every one of these)");
            foreach (var entry in existingEntries)
            {
                var trimmed = entry.Trim().ReplaceLineEndings(" ");
                prompt.AppendLine($"- {(trimmed.Length > MaxContextCharsPerSlot ? trimmed[..MaxContextCharsPerSlot] + "…" : trimmed)}");
            }
            prompt.AppendLine();
        }

        prompt.AppendLine($"Task: write one new {instruction.Name}. Reply with only that content.");

        if (problems is not null && problems.Count > 0)
        {
            prompt.AppendLine();
            prompt.AppendLine("PROBLEM WITH YOUR LAST REPLY");
            foreach (var problem in problems)
            {
                prompt.AppendLine($"- {problem}");
            }
            prompt.AppendLine("Write the content again, corrected.");
        }

        return prompt.ToString();
    }

    private static string BuildContext(ParsedAiTemplate template, AiTemplateSlot slot, IReadOnlyList<AiTemplateSlotValue> slotValues)
    {
        var context = new StringBuilder();
        foreach (var other in template.Slots)
        {
            if (other.Id == slot.Id)
            {
                continue;
            }

            var value = slotValues.FirstOrDefault(v => v.Id == other.Id);
            if (value is null || string.IsNullOrWhiteSpace(value.Content))
            {
                continue;
            }

            var content = value.Content.Length > MaxContextCharsPerSlot
                ? value.Content[..MaxContextCharsPerSlot] + "…"
                : value.Content;
            context.AppendLine($"{other.Name}{(value.Locked ? " (LOCKED - must not be contradicted)" : "")}: {content.Trim()}");
        }
        return context.ToString();
    }
}
