using System.Text.RegularExpressions;

namespace MarkdownHub.Api.Services;

/// <summary>
/// Deterministic checks on one slot's generated text - "AI generates; Markdown Hub verifies".
/// Deliberately a small fixed set rather than a general rules engine: the structure itself is
/// guaranteed by construction, so all that's left to check is that the model filled the one
/// section it was asked to fill, in the requested shape.
/// </summary>
public static class AiTemplateValidator
{
    private static readonly Regex CodeFence = new(@"^```[^\n]*\r?\n(?<body>.*?)\r?\n```\s*$", RegexOptions.Singleline);
    private static readonly Regex Heading = new(@"^#{1,6}\s", RegexOptions.Multiline);
    private static readonly Regex LeftoverPlaceholder = new(@"\{\{[^}]*\}\}");
    private static readonly Regex Preamble = new(@"^(sure|certainly|okay|ok|here'?s|here is|of course)\b[^\n]*$", RegexOptions.IgnoreCase);
    private static readonly Regex BoldNameFormat = new(@"^\*\*[^*]+\*\*\s*\.");
    private static readonly Regex SentenceEnd = new(@"[.!?](\s|$)");

    /// <summary>Strips the wrappers models habitually add - a code fence around the whole answer,
    /// or a "Here's your …:" line followed by a blank line. An ambiguous preamble is left alone so
    /// Check can report it instead of this silently deleting real content.</summary>
    public static string Clean(string raw)
    {
        var text = (raw ?? "").Trim();

        var fence = CodeFence.Match(text);
        if (fence.Success)
        {
            text = fence.Groups["body"].Value.Trim();
        }

        var lines = text.Split('\n');
        if (lines.Length > 2 && Preamble.IsMatch(lines[0].Trim()) && lines[1].Trim().Length == 0)
        {
            text = string.Join('\n', lines.Skip(2)).Trim();
        }

        return text;
    }

    public static AiTemplateValidation Check(string content, AiTemplateInstruction? instruction)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(content))
        {
            problems.Add("You replied with nothing. Write the requested content.");
            return new AiTemplateValidation(false, problems);
        }

        if (Heading.IsMatch(content))
        {
            problems.Add("You added a Markdown heading. Write only the section's own content - the document's headings already exist.");
        }

        if (LeftoverPlaceholder.IsMatch(content))
        {
            problems.Add("Your reply still contains a {{placeholder}}. Write the actual content instead.");
        }

        var firstLine = content.Split('\n')[0].Trim();
        if (Preamble.IsMatch(firstLine))
        {
            problems.Add("Your reply began with a preamble. Reply with only the content itself, no introduction.");
        }

        if (instruction is not null)
        {
            if (instruction.MaxWords is int maxWords && CountWords(content) > maxWords)
            {
                problems.Add($"Your reply is longer than the {maxWords}-word limit. Make it shorter.");
            }

            if (instruction.MaxSentences is int maxSentences && CountSentences(content) > maxSentences)
            {
                problems.Add($"Your reply has more than {maxSentences} sentence(s). Make it shorter.");
            }

            if (instruction.Format is string format && format.Contains("**") && !BoldNameFormat.IsMatch(content))
            {
                problems.Add($"Your reply doesn't match the required format: {format}");
            }
        }

        return new AiTemplateValidation(problems.Count == 0, problems);
    }

    private static int CountWords(string content) =>
        content.Split([' ', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries).Length;

    private static int CountSentences(string content) => Math.Max(1, SentenceEnd.Matches(content).Count);
}
