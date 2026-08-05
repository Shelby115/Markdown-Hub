# AI Knowledge Assistant — Design Specification

## Implementation status (initial scope)

The "Initial Scope" checklist at the bottom of this document is implemented: a right-side
panel (`frontend/src/components/AiAssistantPanel.tsx`) backed by `POST /api/ai/assistant`
(`AiAssistantController`, reusing the `IAiService`/Ollama integration from AI-assisted
editing). You can select the current page plus additional individual pages as context
(search-based picker, `api.suggestWikiLinks`), ask a free-text question or run Summarize /
Expand Topic, review/edit each result card, and Add to Page (current page only) or Ignore.
Every context page is permission-checked server-side before being sent to the model.

**Deferred beyond this pass** (not implemented): folders as context (only individual pages),
RAG/embeddings/semantic search, external web research, the additional card types listed under
"Suggested Cards" (only a plain text-addition card exists), "Add as New Page", structured
per-card metadata/typed JSON beyond `{ title, content }`, and persisting conversation history
across panel close/reopen (it's kept only while the panel stays open in one session). These
match what the design below explicitly calls out as later work, not core to a useful v1.

## Goal

Add an AI-powered knowledge assistant to the application that allows the user to select pages and/or folders as context, ask the AI to research, expand, summarize, organize, or generate information, and review the AI's output before adding anything to the knowledge base.

The assistant should function as a **research and content-generation workspace**, not an autonomous agent. AI-generated content must never modify existing pages without explicit user approval.

## UI

Add a collapsible **AI Assistant side panel** on the right side of the application.

The panel should remain available while browsing/editing pages.

Conceptually:

```text
┌───────────────────────────────┬─────────────────────────────┐
│                               │ AI Assistant                │
│                               │                             │
│       Current Page            │ Context                     │
│                               │ ☑ Current Page              │
│       Markdown content        │ ☑ NPCs/                     │
│                               │   ☑ Gandalf                 │
│                               │   ☑ Aragorn                 │
│                               │ ☐ Locations/                │
│                               │                             │
│                               │ ─────────────────────────── │
│                               │                             │
│                               │ Ask the assistant...        │
│                               │                             │
│                               │ [Research] [Expand]         │
│                               │                             │
│                               │ ─────────────────────────── │
│                               │                             │
│                               │ Suggestions                │
│                               │                             │
│                               │ ┌─────────────────────────┐ │
│                               │ │ New information about   │ │
│                               │ │ Gandalf                 │ │
│                               │ │                         │ │
│                               │ │ ...                     │ │
│                               │ │                         │ │
│                               │ │ [Add] [Edit] [Ignore]   │ │
│                               │ └─────────────────────────┘ │
│                               │                             │
└───────────────────────────────┴─────────────────────────────┘
```

The exact visual implementation should follow the application's existing UI architecture and styling.

## Knowledge Context

The user should be able to explicitly choose what information the AI is allowed to use as context.

Support:

* Current page
* Selected text
* Individual pages
* Folders
* Multiple pages/folders
* Entire knowledge base

The assistant should clearly display the currently selected context.

For folders, the backend should resolve the folder to its contained pages when constructing the AI context.

Do not automatically send the entire knowledge base to Ollama for every request. Context should be explicitly selected or determined through a future search/retrieval mechanism.

## Initial Actions

Provide predefined actions such as:

### Research

Research/expand the selected topic using the available knowledge context and produce useful new information.

### Expand Topic

Take the selected topic or text and propose additional information that would make the existing knowledge more complete.

### Summarize

Summarize the selected pages or context.

### Find Connections

Identify relationships between the selected pages and suggest relevant existing pages that should be linked.

### Generate Ideas

Generate possible additional topics, pages, or sections related to the selected knowledge.

### Ask

Allow the user to enter a natural-language question about the selected knowledge.

The action system should be extensible so additional actions can be added later without redesigning the assistant.

## AI Output Cards

AI-generated content should not be inserted directly into a page.

Instead, each meaningful piece of generated information should appear as an individual **result card** in the assistant panel.

Example:

```text
┌──────────────────────────────────────┐
│ Possible addition                    │
│                                      │
│ ## History                           │
│                                      │
│ Gandalf originally served as...      │
│                                      │
│ Source: AI-generated                 │
│                                      │
│ [Add to Page] [Edit] [Ignore]        │
└──────────────────────────────────────┘
```

Cards should support:

### Add to Page

Allow the user to choose the destination page and insert the content.

If the assistant was opened while editing a page, default to that page.

### Edit

Open the generated content in an editable state before insertion.

The user should be able to modify the AI-generated content and then add the edited version.

### Ignore

Remove/dismiss the suggestion without modifying any page.

### Add as New Page

For suggestions that represent a complete topic, allow the user to create a new page from the generated content.

The AI should be able to suggest a page title, but the user must approve it before creation.

## Existing Content Protection

The assistant must never silently overwrite or modify existing content.

All modifications must follow this flow:

```text
AI generates content
        ↓
Result card
        ↓
User reviews
        ↓
User chooses Add/Edit/Ignore
        ↓
Application modifies knowledge base
```

For inserting content into an existing page, provide a clear indication of where the content will be inserted.

If replacing existing selected text, require explicit confirmation.

## AI Service Architecture

Use the existing `IAiService` abstraction created for the basic AI integration.

The assistant should communicate with the .NET backend rather than directly with Ollama.

```text
Browser
   │
   │ Assistant request
   ▼
.NET API
   │
   ├── Determine context
   ├── Load selected pages
   ├── Construct prompt
   └── Call IAiService
            │
            ▼
         Ollama
```

The AI service should remain provider-independent.

Do not make the frontend aware of Ollama-specific implementation details.

## Context Construction

The backend should construct a structured context for the AI rather than simply concatenating arbitrary page contents.

For example:

```text
KNOWLEDGE CONTEXT

PAGE: Gandalf
PATH: Characters/Gandalf

[page contents]


PAGE: The Fellowship
PATH: Campaign/Events/The Fellowship

[page contents]
```

The AI should be explicitly instructed that this content represents the user's existing knowledge and should distinguish between:

* Facts present in the supplied knowledge
* Reasonable inferences
* Newly generated information
* Information that may require external research

Do not allow the AI to silently present invented information as existing knowledge.

## External Web Research

The initial implementation does not need external web search.

However, design the assistant so that a future research provider can be added.

Potential future flow:

```text
User asks question
       ↓
AI determines external research is useful
       ↓
Search provider
       ↓
AI summarizes/reconciles results
       ↓
Result cards with sources
       ↓
User approves content
       ↓
Add to knowledge base
```

The architecture should not prevent this from being added later.

## Suggested Cards

The assistant should eventually support different result types:

* Text addition
* Page creation
* Suggested link
* Suggested tag
* Suggested property
* Summary
* Research result
* Question/answer
* Conflict/inconsistency warning

Each card should contain enough metadata for the frontend to determine how it should be displayed and what actions are available.

## Structured AI Responses

Prefer structured JSON responses from the AI/backend rather than asking the frontend to parse arbitrary Markdown to determine what suggestions were generated.

For example:

```json
{
  "results": [
    {
      "type": "text",
      "title": "Suggested History Section",
      "content": "## History\n\n...",
      "suggestedPage": "Gandalf"
    },
    {
      "type": "page",
      "title": "Valinor",
      "content": "# Valinor\n\n...",
      "suggestedPage": "Valinor"
    }
  ]
}
```

The exact schema should follow existing project conventions and be designed for future result types.

## Conversation History

The assistant should maintain the current conversation while the panel remains open so that follow-up questions can reference previous responses.

For example:

```text
User:
Tell me about this character.

AI:
...

User:
What other pages are related to this?

AI:
...
```

Conversation history should be scoped appropriately and should not automatically become part of the permanent knowledge base.

A future version may allow conversations to be saved.

## Model Configuration

Use the existing AI model configuration.

The model should be configurable through application configuration rather than hard-coded.

The UI may optionally display the currently selected model.

Do not assume a particular Ollama model.

## Performance

Do not send unnecessary context to the model.

For large pages or folders:

* Apply reasonable context limits.
* Prefer relevant pages/content when possible.
* Design the context-building service so semantic search/RAG can be introduced later.

Streaming AI responses should be supported if practical with the existing architecture, but a complete non-streaming implementation is acceptable for the initial version.

## Security

Respect the application's existing authentication and authorization.

A user must only be able to provide the AI with pages they themselves are authorized to access.

Do not expose Ollama directly to the client.

Do not allow AI-generated content to execute arbitrary HTML, JavaScript, or other executable content when rendered.

Sanitize rendered AI-generated Markdown according to the application's existing content-safety approach.

## Initial Scope

Implement the smallest useful version first:

1. Right-side AI Assistant panel.
2. Select current page as context.
3. Allow additional individual pages to be added as context.
4. Text input for questions.
5. "Summarize" action.
6. "Expand Topic" action.
7. AI results displayed as cards.
8. Add to current page.
9. Edit before adding.
10. Ignore/dismiss.
11. Use the existing `IAiService`/Ollama integration.
12. No automatic modifications to existing pages.
13. No RAG/vector database yet.
14. No external web research yet.

Build the architecture so folders, semantic search, web research, additional card types, and more sophisticated AI actions can be added later without replacing the core implementation.

## Success Criteria

The feature is complete when a user can:

1. Open the AI Assistant beside a page.
2. Select the current page and/or additional pages as context.
3. Ask the AI a question or choose a predefined action.
4. Receive one or more useful AI-generated result cards.
5. Review the generated content.
6. Edit it if desired.
7. Add it to an existing page or create a new page.
8. Ignore unwanted suggestions.
9. Confirm that no AI operation modifies the knowledge base without explicit user action.
