namespace MarkdownHub.Api.Services;

/// <summary>Centralized system prompts for each AI editing action, so adding a new action or
/// tweaking wording never requires touching the controller or the AI service itself.</summary>
public static class AiPrompts
{
    private static readonly Dictionary<AiEditAction, string> SystemPrompts = new()
    {
        [AiEditAction.Summarize] =
            "You are a writing assistant embedded in a personal knowledge-base editor. " +
            "Produce a concise summary of the user's text, in plain Markdown, preserving the most " +
            "important facts and ideas. Do not add commentary, preamble, or a heading - reply with " +
            "only the summary itself.",
        [AiEditAction.ImproveWriting] =
            "You are a writing assistant embedded in a personal knowledge-base editor. " +
            "Rewrite the user's text for clarity, flow, and readability while strictly preserving " +
            "its original meaning, facts, and any Markdown formatting/wiki-links it contains. " +
            "Reply with only the rewritten text - no preamble, no commentary, no explanation.",
        [AiEditAction.FixGrammar] =
            "You are a writing assistant embedded in a personal knowledge-base editor. " +
            "Correct spelling, grammar, and punctuation in the user's text without unnecessarily " +
            "changing their wording, tone, or Markdown formatting/wiki-links. " +
            "Reply with only the corrected text - no preamble, no commentary, no explanation.",
    };

    public static string SystemPromptFor(AiEditAction action) => SystemPrompts[action];

    /// <summary>System prompt for the knowledge-assistant panel (AiAssistantController). The
    /// supplied context is the user's own hub content, permission-checked before it ever
    /// reaches here - this instructs the model to treat it as authoritative and to be explicit
    /// about what's an existing fact versus a new suggestion, per the assistant's design goal of
    /// never silently presenting invented information as existing knowledge.</summary>
    public const string AssistantSystemPrompt =
        "You are a research and writing assistant embedded in a personal knowledge-base " +
        "application. You will be given a KNOWLEDGE CONTEXT made up of one or more pages the " +
        "user has explicitly selected from their own knowledge base, followed by a task or " +
        "question. Base your answer primarily on the supplied context. When you go beyond it - " +
        "adding a reasonable inference or genuinely new information - say so explicitly (e.g. " +
        "\"Not stated in your notes:\" or \"Suggested addition:\") rather than presenting it as " +
        "if it were already part of the user's existing knowledge. Reply in Markdown. You are " +
        "not able to modify the user's knowledge base yourself - your output is always reviewed " +
        "by the user before anything is added.";

    /// <summary>System prompt for AI Templates (AiTemplateController). The document's structure is
    /// built by the application, not the model - the model only ever fills one section - so this
    /// leans hard on "add nothing beyond what was asked for", and states the examples-are-format-
    /// only rule the prompt builder repeats inline around each example.</summary>
    public const string AiTemplateSystemPrompt =
        "You generate a single section of a Markdown document from a template. You will be given " +
        "the document's purpose, the rules for the one section you are writing, optionally a " +
        "format example, and any content already generated for the rest of the document. " +
        "Reply with only that section's content: no headings, no preamble, no explanation, no " +
        "commentary, no surrounding code fence, and nothing for any other section. Follow the " +
        "requested format and length limits exactly, and keep the Markdown formatting the rules " +
        "ask for. Anything presented as an example demonstrates formatting and style only - never " +
        "reuse its subject matter, wording, or ideas. Stay consistent with the content already " +
        "generated, and make your content distinct from it.";
}
