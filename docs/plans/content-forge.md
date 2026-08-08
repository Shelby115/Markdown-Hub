# Implementation Plan: Content Forge

Design source: `docs/AI Generation Template Feature Design.md`

## Overview

Content Forge turns an existing Markdown Hub *template* page into an AI-powered generator.
The template page carries both the output structure (`{{Placeholder}}` markers in ordinary
Markdown) and a block of per-placeholder generation instructions. Markdown Hub parses that
template into an ordered list of **segments** (literal Markdown) and **slots** (one per
placeholder occurrence), generates **one slot at a time** through the existing `IAiService`,
validates each result deterministically, and presents the slots as independently
reroll/improve/lock-able cards. The user assembles nothing manually — Markdown Hub joins the
segments and slot values back into a document and saves it through the ordinary
`api.savePage` path.

Three decisions shape the whole implementation:

1. **The AI never produces the document skeleton.** It only ever fills one slot at a time.
   Structure, section counts, ordering, and headings are enforced by construction rather than
   by asking the model nicely and hoping. This is design principle #3 taken literally, and it
   makes "reroll one component" the *same* code path as initial generation rather than a
   special case.
2. **No new database tables.** Per design §14 and §3, generation is temporary tooling and the
   result is ordinary Markdown. Forge session state (slot values, locks) lives in the React
   panel's state; the backend is stateless and receives the current slot map on every call.
   Nothing to migrate, nothing to clean up, nothing to retain.
3. **A template is a Forge template iff its content contains a ` ```forge ` block.** No new
   `PageMetadata` column, no new admin surface — it reuses the existing `IsTemplate` flag and
   the existing "New page from template" entry point.

## Architecture

```
FileTree "New page from template"
        │  (template has a ```forge block?)
        ├── no  → existing TemplateVariablesModal flow (unchanged)
        └── yes → ContentForgePanel (modal)
                        │
                        │  POST /api/ai/forge/parse   { templatePath }
                        │     → segments[] + slots[] (structure only, no AI)
                        │
                        │  POST /api/ai/forge/generate { templatePath, slotId, slots[] }
                        │     → one slot's content + warnings
                        │        (called once per slot for "Generate all",
                        │         once for Reroll/Improve)
                        │
                        └── Save → api.savePage(newPath, assembledMarkdown)
                                   (ordinary save; versions/search/backlinks all
                                    happen for free)
```

Backend layering follows the existing AI code exactly: a controller under
`Controllers/AI/` that does auth + permission checks and shape validation, delegating to
services under `Services/AI/ContentForge/`, which depend only on `IAiService` — never on
Ollama. Prompts live in `AiPrompts.cs` alongside the existing ones.

`ContentForgeController` mirrors `AiAssistantController`'s security posture: `[Authorize]`,
`PermissionService.HasAtLeastAsync(..., PermissionLevel.View)` on the template path before
reading it, `AiServiceException` → `502 Bad Gateway` with the exception's user-safe message,
and hard caps on everything that comes from the request body.

### Template authoring format

The template page is ordinary Markdown. Structure uses `{{Name}}` placeholders. Instructions
live in a fenced block with the info string `forge`:

````markdown
# Adventure

{{Scene}}

## Interactibles

{{Interactible}}
{{Interactible}}
{{Interactible}}
{{Interactible}}

## Encounter

{{Encounter}}

```forge
Scene:
- Random biome and location.
- Very brief scene-setting description.
- Max words: 60

Interactible:
- One brief interactible.
- Item 1 is mundane; items 2 and 3 have obvious interactions; item 4 hides a secret.
- Format: **Name**. One brief sentence.
- Max words: 30
- Example: **Rusted Lantern**. It still holds a little oil.

Encounter:
- One NPC or monster appropriate to the generated setting.
```
````

Why a fenced block rather than YAML frontmatter: the app has no frontmatter parser today,
the block renders visibly and harmlessly in both the live editor and published/transcluded
HTML, it is exactly the shape the design doc already illustrates in §5, and it needs about
forty lines of parser. The block is stripped from generated output.

**Interaction with the existing `{{Variable}}` system.** `FileTree.tsx` already treats every
`{{Name}}` as a fill-in-the-blank prompt. The rule that keeps both working: a placeholder is
an **AI slot** if the `forge` block has an instruction entry for its name, and a plain
**fill-in variable** otherwise. So a Forge template can still have `{{Author}}` collected via
the existing modal before generation starts, and existing non-Forge templates are completely
unaffected (no `forge` block → no slots → current behavior).

### Repeated placeholders

`{{Interactible}}` four times parses to four slots — `Interactible#1` … `Interactible#4` —
that share one instruction entry. The **count comes from the structure, not from the AI**, so
"four interactibles were generated" is true by construction and needs no validation rule.
Each slot's prompt states its index ("this is item 3 of 4") and receives its already-generated
siblings as context so they stay distinct. "Reroll All" on a group rerolls only that group's
unlocked slots.

## Implementation

### Backend

#### `Services/AI/ContentForge/` (new folder)

**`ForgeTemplate.cs`** — records only (grouped per the project's one-type-per-file rule
exception for records):

```csharp
public record ForgeSegment(string Text);                       // literal markdown between slots
public record ForgeSlot(string Id, string Name, int Index, int Count);
public record ForgeInstruction(string Name, List<string> Rules, string? Format,
                               string? Example, int? MaxWords, int? MaxSentences);
public record ForgeTemplate(List<ForgeElement> Elements,
                            Dictionary<string, ForgeInstruction> Instructions,
                            List<string> FillInVariables);
public record ForgeElement(string? LiteralText, ForgeSlot? Slot);  // ordered; exactly one set
```

**`ForgeTemplateParser.cs`** — pure, no I/O, no DI. `Parse(string templateContent)`:
- Extracts and removes the ` ```forge ` block; parses it as `Name:` headers followed by
  `- rule` bullets. Recognizes four typed rule prefixes case-insensitively — `Format:`,
  `Example:`, `Max words: N`, `Max sentences: N` — and keeps everything else as free-text
  rules passed verbatim to the model.
- Walks the remaining Markdown, splitting on `{{Name}}` into an ordered `ForgeElement` list.
- Names with an instruction entry become slots (numbered per name); names without become
  `FillInVariables`.
- Caps: 40 slots, 20 distinct names, 8 KB of instruction text. Over-cap returns a validation
  error rather than truncating silently.

**`ForgePromptBuilder.cs`** — builds the user prompt for one slot from the four layers in
design §6, with clearly separated, explicitly labeled blocks:

```
TEMPLATE PURPOSE      (the template's leading heading/prose, trimmed)
SECTION TO GENERATE   (name, "item 3 of 4", its rules, its Format:)
FORMAT EXAMPLE        (Example:, wrapped in an explicit "style only, never reuse" fence)
ALREADY GENERATED     (other slots' current values, locked ones marked LOCKED)
```

Design §7's failure mode — the model parroting the example's subject — is addressed in two
places: the global system prompt states that examples demonstrate formatting only, and the
example is wrapped inline with a "do not reuse this example's subject matter" instruction so
it survives being far from the system prompt in a long context.

For `Improve`, the same prompt plus a `CURRENT TEXT` block and an instruction to revise while
keeping the same subject. For a correction retry, plus a `PROBLEM WITH YOUR LAST REPLY` block
naming the failed check.

**`ForgeValidator.cs` / `ForgeValidationResult.cs`** — deterministic checks against one
slot's generated text. Deliberately a small fixed set rather than a general rules engine:
- Non-empty after trimming.
- No Markdown headings (`^#{1,6} `) — the AI must never invent structure.
- No leftover `{{...}}` markers.
- No leading preamble line (`Here is…`, `Sure,…`) — strip it if it is followed by a blank
  line, fail if ambiguous.
- `Max words` / `Max sentences` if declared.
- `Format:` containing `**Name**.` requires the result to start with a bold run followed by
  a period.

Returns `ForgeValidationResult(bool IsValid, List<string> Problems)`.

**`ContentForgeService.cs`** — orchestration for one slot:
1. Build prompt → `IAiService.CompleteAsync`.
2. Post-process (trim, strip code fences the model may have wrapped the answer in).
3. Validate. On failure, **one** correction retry with the problems fed back.
4. If it still fails, return the content anyway with `Warnings` populated — per design §12,
   a failed validation must never blank out a result. The UI shows a warning badge and the
   user can reroll or edit.
5. `AiServiceException` propagates to the controller untouched; the caller's existing slot
   value is never modified (the client only overwrites a slot on a 200).

#### `Controllers/AI/ContentForgeController.cs`

| Route | Verb | Purpose |
|---|---|---|
| `/api/ai/forge/parse` | POST | Permission-check + read template, return parsed segments/slots/instructions/fill-in variables. No AI call — so it works and gives a useful error even with Ollama down. |
| `/api/ai/forge/generate` | POST | Generate/reroll/improve **one** slot. |

`GenerateRequest(string TemplatePath, string SlotId, ForgeMode Mode, List<ForgeSlotValue> Slots,
Dictionary<string,string>? Variables)` where `ForgeMode` is `Generate | Improve`.
The controller re-reads and re-parses the template server-side on every call rather than
trusting a client-supplied structure — the client can only name a slot, never inject
instructions.

"Generate all" is N sequential client calls, not a server loop. This gives per-slot progress,
lets the user cancel or stop partway, keeps each request inside the existing 60s Ollama
timeout, and means a failure on slot 5 leaves slots 1–4 intact.

Reuse the existing `GET /api/ai/assistant/status` for the "Ollama not found" upfront state —
no new status endpoint.

#### `Controllers/AI/ContentForgeModels.cs`

Request/response records, following `AiAssistantModels.cs`.

#### Modified backend files

- **`Services/AI/AiPrompts.cs`** — add `ForgeSystemPrompt` (design §6 layer 1: follow the
  requested structure, add nothing unrequested, preserve Markdown, never copy examples, no
  preamble or explanation, no headings).
- **`Program.cs`** — `builder.Services.AddScoped<ContentForgeService>();`. The parser,
  prompt builder, and validator are static/pure and need no registration.
- **`Controllers/FilesController.cs`** — `TemplateInfo` gains `bool IsForgeTemplate` so the
  file tree can label Forge templates in its dropdown. Computed by checking the file for a
  ` ```forge ` fence; to avoid reading every template file on every tree load, compute it from
  the already-loaded `PageMetadata` only when cheap — otherwise let the frontend discover it
  from the parse call. *(Simplest correct option: leave `TemplateInfo` alone and have the
  frontend call `/api/ai/forge/parse` after a template is chosen; it returns zero slots for a
  non-Forge template and the existing flow continues. Prefer this unless the dropdown label
  is wanted.)*

### Frontend

**`components/ContentForgePanel.tsx`** (new) — a modal, styled like the existing
`.modal` / `TemplateVariablesModal` pattern, launched from `FileTree`'s create-from-template
flow. State:

```ts
interface SlotState {
  id: string; name: string; index: number; count: number;
  content: string; locked: boolean; busy: boolean;
  warnings: string[]; error: string | null;
}
```

Layout mirrors design §9: slots grouped by name; a group header with **Reroll All** when
`count > 1`; each card showing the rendered text plus **Reroll**, **Improve**, **Lock/Unlock**,
and inline edit (reuse the `ai-result-card` textarea pattern from `AiAssistantPanel`). A
footer with **Generate All**, a page-name input, a target-folder display, and **Save as page**.

Behavior rules that fall out of the design:
- A locked slot is skipped by Generate All / Reroll All and is sent to the backend flagged
  `locked: true` so it becomes context (design §10).
- A slot's content is only replaced on a successful response; failures set `error` on that
  card and leave the previous value (design §12, principle #7).
- Generate All runs sequentially so each slot sees the previous ones (design §8), with an
  abort control.

**`components/contentForge.ts`** (new) — pure helpers, unit-testable without React:
`assembleDocument(elements, slots, variables)` joins literal segments and slot values (and
substitutes any fill-in variables), and `groupSlots(slots)` groups by name for rendering.

**Modified frontend files**
- `api/client.ts` — `ForgeParseResult`, `ForgeSlotValue`, `ForgeGenerateResult` interfaces
  plus `forgeParse()` / `forgeGenerate()` calls, following the existing `request<T>` pattern.
- `components/FileTree.tsx` — in `commitCreate`, after loading the chosen template, call
  `forgeParse`; if it returns slots, open `ContentForgePanel` instead of
  `TemplateVariablesModal`; on save, route through the existing `createPage(folder, name,
  content)` so tree reload/error handling is unchanged. Fill-in variables returned by the
  parse are collected by the existing modal *first*, then handed to the Forge panel.
- `styles/index.css` — `.forge-*` classes, reusing the existing `ai-result-card` and `modal`
  visual language rather than inventing a new one.

### Database

**No changes.** No new tables, columns, entities, or `DatabaseMigrations.cs` statements.
Forge templates are ordinary pages already flagged `IsTemplate`; sessions are ephemeral;
results are ordinary saved documents that pick up version history, search indexing, and
backlinks through the existing save path.

### Other

- **Documentation** — `README.md`: a Content Forge section with the ` ```forge ` authoring
  syntax, the recognized rule prefixes, and a full worked example. `CLAUDE.md`: a "Content
  Forge Design" entry under the task list plus a bullet in "What's implemented".
- **Configuration** — none. Reuses `Ai:Ollama:*` and the admin model override.
  Note in the README that a template with many slots means many sequential model calls, and
  that `Ai:Ollama:TimeoutSeconds` applies per slot.

## Files to Modify

| File | Changes |
|------|---------|
| `backend/MarkdownHub.Api/Services/AI/AiPrompts.cs` | Add `ForgeSystemPrompt` |
| `backend/MarkdownHub.Api/Program.cs` | Register `ContentForgeService` |
| `frontend/src/api/client.ts` | Forge types + `forgeParse` / `forgeGenerate` |
| `frontend/src/components/FileTree.tsx` | Route a Forge template to `ContentForgePanel`; pass fill-in variables through |
| `frontend/src/styles/index.css` | `.forge-*` styles |
| `README.md` | Content Forge usage + authoring syntax |
| `CLAUDE.md` | Feature entry and implemented-list bullet |

## Files to Create

| File | Purpose |
|------|---------|
| `backend/MarkdownHub.Api/Services/AI/ContentForge/ForgeTemplate.cs` | Parsed-template records |
| `backend/MarkdownHub.Api/Services/AI/ContentForge/ForgeTemplateParser.cs` | Structure + instruction-block parsing |
| `backend/MarkdownHub.Api/Services/AI/ContentForge/ForgePromptBuilder.cs` | Layered prompt construction |
| `backend/MarkdownHub.Api/Services/AI/ContentForge/ForgeValidator.cs` | Deterministic per-slot checks |
| `backend/MarkdownHub.Api/Services/AI/ContentForge/ForgeValidationResult.cs` | Validation result record |
| `backend/MarkdownHub.Api/Services/AI/ContentForge/ContentForgeService.cs` | Generate → validate → correct orchestration |
| `backend/MarkdownHub.Api/Controllers/AI/ContentForgeController.cs` | `/api/ai/forge/parse`, `/api/ai/forge/generate` |
| `backend/MarkdownHub.Api/Controllers/AI/ContentForgeModels.cs` | Request/response records |
| `backend/MarkdownHub.Api.Tests/Services/ForgeTemplateParserTests.cs` | Parser coverage |
| `backend/MarkdownHub.Api.Tests/Services/ForgeValidatorTests.cs` | Validation coverage |
| `backend/MarkdownHub.Api.Tests/Services/ContentForgeServiceTests.cs` | Retry/fallback coverage with a fake `IAiService` |
| `backend/MarkdownHub.Api.Tests/Controllers/ContentForgeControllerTests.cs` | Auth, permissions, error mapping |
| `frontend/src/components/ContentForgePanel.tsx` | Forge review/iterate UI |
| `frontend/src/components/contentForge.ts` | `assembleDocument` / `groupSlots` |
| `frontend/src/components/contentForge.test.ts` | Assembly coverage |
| `frontend/src/components/ContentForgePanel.test.tsx` | Panel behavior coverage |

## Data Flow

**Initial generation**

1. User picks a template in the file tree's create-from-template dropdown, types a page name.
2. Frontend `POST /api/ai/forge/parse { templatePath }`.
3. Backend: View permission check → `MarkdownFileService.ReadAsync` → `ForgeTemplateParser.Parse`
   → returns ordered elements, slots, and fill-in variable names.
4. Zero slots → existing flow, done. Otherwise: any fill-in variables are collected via the
   existing `TemplateVariablesModal`, then `ContentForgePanel` opens with empty slots.
5. **Generate All** loops slots in document order. For each unlocked slot:
   `POST /api/ai/forge/generate { templatePath, slotId, mode: "Generate", slots }` where
   `slots` carries every slot's current content and locked flag.
6. Backend re-reads/re-parses the template, builds the layered prompt including
   already-generated and locked siblings, calls `IAiService.CompleteAsync`, post-processes,
   validates, retries once on failure, returns `{ content, warnings }`.
7. Panel writes the returned content into that slot and moves to the next.

**Reroll / Improve** — identical to step 5 for a single slot, with `mode` set accordingly.
Every other slot is unchanged and travels along as context.

**Save** — `assembleDocument` joins literal segments with slot values, then the existing
`createPage` → `api.savePage` path runs. From that moment the result is an ordinary document:
versioned, indexed, backlinked, editable.

## Testing

**Backend (xUnit, existing conventions)**
- `ForgeTemplateParserTests` — single and repeated placeholders; slot numbering and counts;
  instruction block extracted and removed from output; typed rules (`Format:`, `Example:`,
  `Max words:`) parsed; a name without an instruction becomes a fill-in variable, not a slot;
  a template with no `forge` block yields zero slots; cap violations rejected.
- `ForgeValidatorTests` — heading rejected; leftover `{{...}}` rejected; word/sentence limits;
  `**Name**.` format rule; preamble stripping; valid content passes untouched.
- `ContentForgeServiceTests` — with a fake `IAiService` (same pattern as
  `AiAssistantControllerTests`'s `FakeAiService`): invalid-then-valid triggers exactly one
  retry; invalid-twice returns the content plus warnings rather than throwing; the correction
  prompt names the failed check; locked slots appear in the prompt marked as locked.
- `ContentForgeControllerTests` — unauthenticated → 401; template the user can't View → 403;
  missing template → 404; unknown slot id → 400; `AiServiceException` → 502 with its message.

**Frontend (Vitest + Testing Library)**
- `contentForge.test.ts` — assembly preserves literal Markdown exactly, substitutes slots in
  order, handles repeated placeholders and empty slots.
- `ContentForgePanel.test.tsx` — locked slot is skipped by Generate All and sent as locked;
  a failed reroll preserves the previous content and shows an error on that card only;
  warnings render; Save calls through with the assembled document.

**Manual verification**
Author the Adventure template from design §4, run Generate All against a real Ollama, confirm
four distinct interactibles in the requested `**Name**.` format, lock the encounter, reroll
the scene, and confirm the saved page opens and edits normally.

## Risks / Considerations

- **Latency.** Per-slot generation means a 7-slot template is 7 sequential local-model calls —
  potentially a minute or more on a 20b model. This is the deliberate cost of deterministic
  structure. Mitigated by per-slot progress, usable partial results, and an abort control, but
  it is the main thing that could feel slow. (A future optimization: generate slots that share
  no context dependency in parallel.)
- **Existing `{{Variable}}` behavior.** `FileTree.tsx` currently prompts for *every* `{{Name}}`.
  The change must be surgical — a template with no `forge` block has to behave exactly as it
  does today, or existing user templates regress.
- **Small local models and instruction adherence.** Validation catches format violations, but a
  model that ignores "be concise" produces valid-but-poor output. The Improve/Reroll loop is the
  answer; expect prompt-wording iteration after real use.
- **Example leakage** (design §7) is the highest-risk *quality* failure. Addressed in two places
  (system prompt + inline fencing), but worth checking explicitly during manual verification.
- **Ollama unavailable.** `/parse` deliberately needs no AI, so the panel can open and explain
  the situation rather than failing opaquely; the existing assistant-status check supplies the
  upfront message.
- **No session persistence.** Closing the modal loses in-progress generation. Acceptable for a
  first version (design §3), but worth knowing. `sessionStorage` would be a cheap follow-up.
- **Template read on every slot call** — re-reading a small file per slot is negligible and buys
  the security property that clients cannot inject instructions.

## Open Questions

1. **Instruction authoring format.** Recommended: a fenced ` ```forge ` block inside the
   template page, as illustrated above — no new parser dependency, renders harmlessly, matches
   the design doc's own examples. Alternative: YAML frontmatter (would need a frontmatter
   parser the app does not currently have, and would show up as raw text in the live editor).
2. **Slot-at-a-time vs one-shot generation.** Recommended: slot-at-a-time, for guaranteed
   structure and a unified reroll path. The tradeoff is the latency noted above. A one-shot
   "generate the whole document, then split it" first pass would be faster but reintroduces
   exactly the structural failures §11 and §12 exist to prevent.
3. **Entry point.** Recommended: the existing file-tree "New page from template" flow only, for
   the first version. Should Content Forge *also* be reachable from the editor toolbar to forge
   content into an already-open page (the way the AI Assistant's "Add to page" works)? That is
   straightforward to add later but changes the panel's save path.

## Implementation Order

1. `ForgeTemplateParser` + records, with tests. Everything else depends on the parsed shape.
2. `ForgeValidator`, with tests.
3. `ForgePromptBuilder` + `AiPrompts.ForgeSystemPrompt`.
4. `ContentForgeService` (generate → validate → retry → warnings), with fake-`IAiService` tests.
5. `ContentForgeController` + models + DI registration, with controller tests. Backend is now
   independently exercisable via curl/Swagger before any UI exists.
6. `contentForge.ts` assembly helpers, with tests.
7. `api/client.ts` types and calls.
8. `ContentForgePanel.tsx` — parse/display first, then Generate All, then Reroll/Improve/Lock,
   then Save.
9. `FileTree.tsx` wiring, being careful that the non-Forge path is byte-for-byte unchanged.
10. Styles.
11. Manual end-to-end run against real Ollama with the design §4 Adventure template.
12. `README.md` + `CLAUDE.md`.
