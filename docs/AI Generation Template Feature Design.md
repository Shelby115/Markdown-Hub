# Markdown Hub --- AI Templates

## Design Specification

**Status:** Concept / Design
**Feature Name:** AI Templates
**Purpose:** Turn Markdown templates into reusable, AI-powered content generators.

------------------------------------------------------------------------

## 1. Overview

AI Templates allow a user to define a Markdown template containing named placeholders.
Markdown Hub uses the template's structure and rules to construct a controlled AI prompt,
generates the content, validates the result, and presents the generated sections as independent editable/regeneratable components.

The user is not maintaining a database of generated building blocks.
The primary artifact is the finished content they choose to keep.

### Core principle

> The template defines the rules.
> The AI provides the creativity.
> Markdown Hub provides the structure, validation, and controls.

AI should enhance generation without becoming a single point of failure.

------------------------------------------------------------------------

## 2. Goals

-   Allow users to create reusable AI-powered Markdown templates.
-   Let users describe the structure and constraints of generated
    content.
-   Produce consistent formatting and structure.
-   Prevent examples from being copied literally as generated content.
-   Allow multiple generated sections to remain contextually connected.
-   Allow individual sections to be rerolled without regenerating
    everything.
-   Allow individual sections to be improved.
-   Allow sections to be locked so subsequent generation preserves them.
-   Provide deterministic/non-AI fallback behavior where practical.
-   Validate generated content against the template's requirements.
-   Save the final result as ordinary Markdown Hub content.
-   Integrate with the existing Markdown Hub templating system and AI
    Assistant.

------------------------------------------------------------------------

## 3. Non-Goals for the Initial Version

The first version should **not** require:

-   A separate collection-management system.
-   A database of reusable AI-generated components.
-   Users to manually maintain large libraries of generated values.
-   AI to be perfect at contextual selection.
-   A new standalone content format.

A future version may allow saved Markdown content to become a reusable source for generation.

------------------------------------------------------------------------

## 4. Example Use Case

A user creates an Adventure template containing:

-   A random biome.
-   A random location.
-   A very brief scene-setting description.
-   Four interactibles:
    -   One mundane/no meaningful consequence.
    -   Two with obvious interactions.
    -   One containing a secret.
-   One NPC or monster.
-   One environmental secret.

The desired output might look like:

> You come across a small hill with orange dirt on one side and a
> mineshaft carved into the side of it. It's boarded up and looks
> abandoned.
>
> **Minecart**. There's a half-buried minecart in front of the entrance.
> *It contains 2 pickaxes half buried.*

The important characteristics are:

-   The structure is consistent.
-   The prose is concise.
-   Interactibles follow the requested format.
-   Generated content is varied.
-   The example teaches style/format but is not repeatedly copied.

------------------------------------------------------------------------

## 5. Template Model

An AI Template consists of:

### Structure

The Markdown structure the final result should follow.

Example:

``` text
# Adventure

{{Scene}}

## Interactibles

{{Interactible}}
{{Interactible}}
{{Interactible}}
{{Interactible}}

## Encounter

{{Encounter}}

## Secret

{{Secret}}
```

### Generation instructions

Instructions describing what each placeholder should produce.

Example:

``` text
Scene:
- Random biome and location.
- Very brief scene-setting description.

Interactible:
- Generate four distinct interactibles.
- One mundane.
- Two obvious interactions.
- One hidden/secret interaction.
- Format: **Name**. One brief sentence.
- Keep descriptions concise.

Encounter:
- One NPC or monster appropriate to the generated setting.

Secret:
- One secret appropriate to the location and other generated content.
```

The exact authoring syntax is TBD. The system should favor a simple
authoring experience over requiring users to write complex prompt
syntax.

------------------------------------------------------------------------

## 6. AI Prompt Construction

Markdown Hub should construct the AI request from several layers:

1.  **Global AI Template instructions**
    -   Follow requested structure.
    -   Do not add unrequested sections.
    -   Preserve Markdown formatting.
    -   Do not copy examples literally.
    -   Keep generated content within requested constraints.
    -   Do not provide explanations or reasoning unless requested.
2.  **Template instructions**
    -   Overall purpose and structure.
    -   Formatting requirements.
    -   Generation constraints.
3.  **Placeholder instructions**
    -   What each section should contain.
    -   Quantity.
    -   Format.
    -   Constraints.
4.  **Generation context**
    -   Other sections already generated.
    -   Locked sections.
    -   Previously generated content.
    -   Relevant relationships between sections.

The user should not need to manually construct this complete AI prompt.

------------------------------------------------------------------------

## 7. Examples

Examples should be explicitly treated as **format/style examples**, not
content requirements.

The generated prompt should clearly distinguish:

-   Rules/instructions.
-   Examples.
-   Actual generation context.

The system should tell the model that examples demonstrate formatting
and style and should not be reused unless explicitly requested.

This addresses a common failure mode where an AI repeatedly generates
the same subject from an example.

------------------------------------------------------------------------

## 8. Contextual Generation

Generation should occur with awareness of the rest of the document.

For example, if the generated content contains:

-   Forest
-   Abandoned mine
-   Witch

then a subsequently generated Secret should receive that context.

The AI might therefore generate:

> A hidden passage beneath the mine leads to the witch's old laboratory.

rather than an unrelated generic secret.

### Important constraint

AI contextual generation should be **controlled rather than trusted
blindly**.

Markdown Hub should provide the relevant context and constraints, but
the system should not assume the AI will always make a good choice.

------------------------------------------------------------------------

## 9. Independent Generation and Rerolling

Every generated placeholder should be treated as an independently
addressable component.

The UI should provide actions such as:

-   **Reroll** --- generate a different result.
-   **Improve** --- revise the existing result while preserving its
    general concept.
-   **Lock** --- prevent the component from changing during future
    generation.
-   **Unlock** --- allow the component to change again.

Example:

``` text
Scene
[ Reroll ] [ Lock ]

Interactibles
[ Reroll All ]

  **Minecart**. ...                 [ Reroll ] [ Improve ] [ Lock ]
  **Warning Sign**. ...             [ Reroll ] [ Improve ] [ Lock ]
  **Collapsed Support**. ...        [ Reroll ] [ Improve ] [ Lock ]
  **Strange Stone**. ...            [ Reroll ] [ Improve ] [ Lock ]

Encounter
  **Old Prospector**. ...           [ Reroll ] [ Improve ] [ Lock ]

Secret
  **Hidden Passage**. ...            [ Reroll ] [ Improve ] [ Lock ]
```

Rerolling one component should not regenerate unrelated components.

------------------------------------------------------------------------

## 10. Locked Content

Locked components become part of the generation context.

For example:

1.  Generate adventure.
2.  User likes the NPC.
3.  User locks the NPC.
4.  User rerolls the location.
5.  AI receives the locked NPC as context and generates a new compatible
    location.

This enables iterative creative workflows without forcing the user to
start over.

------------------------------------------------------------------------

## 11. Validation

Generated output should be validated against the template's known
requirements where possible.

Examples:

-   Correct number of sections.
-   Required placeholders were fulfilled.
-   Required Markdown formatting is present.
-   Four interactibles were generated.
-   Interactible names are bold.
-   Descriptions remain within expected length.
-   No unexpected sections were added.

Validation should be deterministic where possible.

If validation fails, Markdown Hub can ask the AI to correct the response
rather than immediately showing invalid output.

### Principle

> **AI generates; Markdown Hub verifies.**

------------------------------------------------------------------------

## 12. Failure and Fallback

AI failure should never make the feature frustrating.

Potential failures include:

-   Timeout.
-   Invalid response.
-   Missing sections.
-   Incorrect structure.
-   Poor adherence to constraints.
-   AI service unavailable.

The system should attempt correction when appropriate.

For failures that cannot be corrected, the UI should preserve the
existing valid content and allow the user to retry or reroll that
specific component.

A failed AI request should never destroy a good result.

------------------------------------------------------------------------

## 13. Generation Workflow

``` text
User selects template
        ↓
Markdown Hub parses structure
        ↓
Build generation instructions
        ↓
AI generates content
        ↓
Validate result
        ↓
 ┌───────────────┐
 │ Valid         │──────→ Display result
 │               │
 │ Invalid       │──────→ AI correction
 └───────────────┘
                          ↓
                     Display result
                          ↓
                User reviews / iterates
                    ↓      ↓      ↓
                 Reroll  Improve  Lock
                          ↓
                    Save as Markdown
```

------------------------------------------------------------------------

## 14. Saving

Once the user is satisfied, the generated result should become a normal
Markdown Hub document.

The generation process is temporary tooling; the resulting content is
not a special proprietary artifact.

Example:

``` text
Adventures/
└── The Abandoned Mine.md
```

The generated Markdown should be editable normally after saving.

------------------------------------------------------------------------

## 15. Future Possibilities

These should not be required for the initial implementation but should
remain compatible with the design.

### Reusable content sources

Existing Markdown pages could eventually be used as generation sources.

For example:

``` text
{{NPC}}
```

could eventually select from existing NPC pages.

### Tags and metadata

Markdown frontmatter could allow filtering:

``` text
{{Interactible#Forest}}
```

### Relationships

Existing Markdown links could provide contextual relationships between
generated content.

### Weighted/random selection

Templates could eventually support deterministic weighted choices.

### Seeds

A generation seed could allow a result to be reproduced.

### AI selection

AI could eventually choose from existing Markdown content rather than
inventing new content.

These are extensions, not prerequisites for AI Template.

------------------------------------------------------------------------

## 16. Design Principles

1.  **Templates should be simple to author.**
2.  **AI should provide creativity, not enforce structure.**
3.  **Markdown Hub should enforce structure wherever possible.**
4.  **Examples teach formatting, not content.**
5.  **Generated components should remain independently controllable.**
6.  **Users should be able to iteratively refine results instead of
    starting over.**
7.  **Good generated content should never be lost because of a failed
    reroll.**
8.  **The final result should remain ordinary Markdown.**
9.  **Advanced database/collection functionality should be optional
    future functionality, not required to use the feature.**

------------------------------------------------------------------------

## 17. Initial Implementation Scope

The first implementation should focus on proving the core loop:

1.  Define a template.
2.  Define placeholder instructions.
3.  Generate a complete result through the existing AI Assistant.
4.  Validate basic structure.
5.  Display generated components independently.
6.  Reroll an individual component.
7.  Lock a component.
8.  Save the final result as Markdown.

Everything else can build on this foundation.
