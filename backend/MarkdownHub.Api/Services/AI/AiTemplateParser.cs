using System.Text.RegularExpressions;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Turns a template page into structure (ordered literal text + slots) and per-placeholder
/// instructions. Pure - no I/O, no DI - so the same parse runs on every request without the
/// client ever being able to supply the structure itself.
/// </summary>
public static class AiTemplateParser
{
    public const int MaxSlots = 40;
    public const int MaxDistinctNames = 20;
    public const int MaxInstructionChars = 8000;
    private const int MaxPurposeChars = 500;

    private static readonly Regex InstructionBlock =
        new(@"^```ai-template[ \t]*\r?\n(?<body>.*?)^```[ \t]*$\r?\n?", RegexOptions.Multiline | RegexOptions.Singleline);
    private static readonly Regex Placeholder = new(@"\{\{([^}]+)\}\}");
    private static readonly Regex InstructionHeader = new(@"^\s*(?<name>[^:\r\n]+):\s*$");
    private static readonly Regex InstructionBullet = new(@"^\s*[-*]\s+(?<rule>.+?)\s*$");

    public static ParsedAiTemplate Parse(string templateContent)
    {
        var content = templateContent ?? "";
        var match = InstructionBlock.Match(content);
        var instructions = match.Success ? ParseInstructions(match.Groups["body"].Value) : [];
        var structure = match.Success ? content.Remove(match.Index, match.Length).TrimEnd() : content;

        var elements = new List<AiTemplateElement>();
        var fillInVariables = new List<string>();
        var occurrences = CountOccurrences(structure, instructions);

        var seen = new Dictionary<string, int>();
        var position = 0;
        foreach (Match placeholder in Placeholder.Matches(structure))
        {
            if (placeholder.Index > position)
            {
                elements.Add(new AiTemplateElement(structure[position..placeholder.Index], null));
            }
            position = placeholder.Index + placeholder.Length;

            var name = placeholder.Groups[1].Value.Trim();
            if (!instructions.ContainsKey(name))
            {
                // No instruction entry: this stays an ordinary fill-in-the-blank variable, handled
                // by the existing template flow rather than by the AI.
                if (!fillInVariables.Contains(name))
                {
                    fillInVariables.Add(name);
                }
                elements.Add(new AiTemplateElement(placeholder.Value, null));
                continue;
            }

            seen[name] = seen.GetValueOrDefault(name) + 1;
            elements.Add(new AiTemplateElement(null, new AiTemplateSlot($"{name}#{seen[name]}", name, seen[name], occurrences[name])));
        }

        if (position < structure.Length)
        {
            elements.Add(new AiTemplateElement(structure[position..], null));
        }

        var slotCount = elements.Count(e => e.Slot is not null);
        if (slotCount > MaxSlots)
        {
            throw new AiTemplateParseException($"This template has {slotCount} AI placeholders; the limit is {MaxSlots}.");
        }

        return new ParsedAiTemplate(elements, instructions, fillInVariables, ExtractPurpose(structure));
    }

    private static Dictionary<string, int> CountOccurrences(string structure, Dictionary<string, AiTemplateInstruction> instructions)
    {
        var counts = new Dictionary<string, int>();
        foreach (Match placeholder in Placeholder.Matches(structure))
        {
            var name = placeholder.Groups[1].Value.Trim();
            if (instructions.ContainsKey(name))
            {
                counts[name] = counts.GetValueOrDefault(name) + 1;
            }
        }
        return counts;
    }

    private static Dictionary<string, AiTemplateInstruction> ParseInstructions(string body)
    {
        if (body.Length > MaxInstructionChars)
        {
            throw new AiTemplateParseException($"The ai-template instruction block is {body.Length} characters; the limit is {MaxInstructionChars}.");
        }

        var result = new Dictionary<string, AiTemplateInstruction>();
        string? currentName = null;
        var rules = new List<string>();
        string? format = null;
        string? example = null;
        int? maxWords = null;
        int? maxSentences = null;

        void Commit()
        {
            if (currentName is null)
            {
                return;
            }
            result[currentName] = new AiTemplateInstruction(currentName, rules, format, example, maxWords, maxSentences);
            rules = [];
            format = null;
            example = null;
            maxWords = null;
            maxSentences = null;
        }

        foreach (var line in body.Replace("\r\n", "\n").Split('\n'))
        {
            var bullet = InstructionBullet.Match(line);
            if (bullet.Success && currentName is not null)
            {
                var rule = bullet.Groups["rule"].Value;
                if (TryTakePrefix(rule, "Format:", out var formatValue))
                {
                    format = formatValue;
                }
                else if (TryTakePrefix(rule, "Example:", out var exampleValue))
                {
                    example = exampleValue;
                }
                else if (TryTakePrefix(rule, "Max words:", out var wordsValue) && int.TryParse(wordsValue, out var words))
                {
                    maxWords = words;
                }
                else if (TryTakePrefix(rule, "Max sentences:", out var sentencesValue) && int.TryParse(sentencesValue, out var sentences))
                {
                    maxSentences = sentences;
                }
                else
                {
                    rules.Add(rule);
                }
                continue;
            }

            var header = InstructionHeader.Match(line);
            if (header.Success)
            {
                Commit();
                currentName = header.Groups["name"].Value.Trim();
                if (result.Count >= MaxDistinctNames && !result.ContainsKey(currentName))
                {
                    throw new AiTemplateParseException($"This template defines more than {MaxDistinctNames} placeholder names.");
                }
            }
        }

        Commit();
        return result;
    }

    private static bool TryTakePrefix(string rule, string prefix, out string value)
    {
        if (rule.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            value = rule[prefix.Length..].Trim();
            return value.Length > 0;
        }
        value = "";
        return false;
    }

    /// <summary>The prose before the first placeholder - what the document as a whole is - so the
    /// model generating a single slot still knows what it's contributing to.</summary>
    private static string ExtractPurpose(string structure)
    {
        var firstPlaceholder = Placeholder.Match(structure);
        var lead = (firstPlaceholder.Success ? structure[..firstPlaceholder.Index] : structure).Trim();
        return lead.Length > MaxPurposeChars ? lead[..MaxPurposeChars] : lead;
    }
}
