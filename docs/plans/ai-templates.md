# Implementation Plan: AI Templates

Design source: `docs/AI Generation Template Feature Design.md`

> Supersedes the earlier draft at `docs/plans/content-forge.md` (same design, different working
> name and a couple of details that didn't match the codebase). Delete that file once this plan
> is approved.

## Overview

An AI Template is an ordinary Markdown Hub template page (already flagged `IsTemplate`) that
carries two things: the output structure, written as normal Markdown with `{{Placeholder}}`
markers, and a fenced ` ```ai-template ` block of per-placeholder generation instructions.

Markdown Hub parses that page into an ordered list of **literal segments** and **slots** (one per
placeholder occurrence), generates **one slot at a time** through the existing `IAiService`,
validates each result deterministically, and shows the slots as independently
reroll/improve/lock-able cards. On save, the segments and slot values are joined and written
through the ordinary create-page path.

Three decisions shape the whole implementation:

1. **The AI never produces the document skeleton.** It only ever fills one slot. Structure,
   section counts, ordering, and headings are enforced by construction rather than by asking the
   model nicely. This is design principle #3 taken literally, and it makes "reroll one component"
   the *same* code path as initial generation rather than a special case.
2. **No new database tables.** Per design §3 and §14, generation is temporary tooling and the
   result is ordinary Markdown. Session state (slot values, locks) lives in the React panel; the
   backend is stateless and receives the current slot map on every call. Nothing to migrate,
   nothing to retain, nothing to clean up.
3. **A template is an AI Template iff its content contains an ` ```ai-template ` block.** No new
   `PageMetadata` column, no new admin surface, no change to `TemplateInfo` — it reuses the
   existing `IsTemplate` flag and the existing "New page from template" entry point.

## Architecture

```
FileTree "New page from template"
        │  POST /api/ai/template/parse { templatePath }   ← structure only, no AI call
        ├── 0 slots → existing TemplateVariablesModal flow (unchanged)
        └── N slots → AiTemplatePanel (modal)
                        │
                        │  POST /api/ai/template/generate { templatePath, slotId, mode, slots }
                        │     → one slot's content + warnings
                        │        (once per slot for "Generate all"; once for Reroll/Improve)
                        │
                        └── Save → existing createPage(folder, name, assembledMarkdown)
                                   (versions/search/backlinks all happen for free)
```

Backend layering follows the existing AI code exactly: a controller under `Controllers/AI/` doing
auth, permission checks, and request-shape validation, delegating to services under
`Services/AI/`, which depend only on `IAiService` — never on Ollama. Prompts go in
`AiPrompts.cs` alongside the existing ones.

**Namespace note:** files in `Services/AI/` currently use the flat `MarkdownHub.Api.Services`
namespace (see `IAiService.cs`, `AiPrompts.cs`), not `...Services.AI`. New service files follow
that existing convention rather than introducing a second one. Controller files use
`MarkdownHub.Api.Controllers.AI`, matching `AiAssistantController.cs`.

`AiTemplateController` mirrors `AiAssistantController`'s security posture: `[ApiController]`
+ `[Authorize]`, `CurrentUserService.GetCurrentAsync` → 401,
`PermissionService.HasAtLeastAsync(..., PermissionLevel.View)` on the template path before
reading it, `AiServiceException` → `502 Bad Gateway` carrying the exception's user-safe message,
and hard caps on everything that arrives in the request body.

### Template authoring format

The template page is ordinary Markdown. Structure uses `{{Name}}`. Instructions live in a fenced
block with the info string `ai-template`:

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

```ai-template
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

Why a fenced block rather than YAML frontmatter: the app has no frontmatter parser today, a
fenced block renders visibly and harmlessly in the live editor and in published/transcluded HTML,
it is close to the shape design §5 already illustrates, and it needs about forty lines of parser.
The block is stripped from the generated document.

**Interaction with the existing `{{Variable}}` system.** `FileTree.tsx:48` currently treats every
`{{Name}}` as a fill-in-the-blank prompt. The rule that keeps both working: a placeholder is an
**AI slot** if the instruction block has an entry for its name, and a plain **fill-in variable**
otherwise. So an AI Template can still have `{{Author}}` collected by the existing modal before
generation starts, and existing templates are completely unaffected (no instruction block → no
slots → today's behavior, byte for byte).

### Repeated placeholders

`{{Interactible}}` four times parses to four slots — `Interactible#1` … `Interactible#4` — sharing
one instruction entry. The **count comes from the structure, not from the AI**, so "four
interactibles were generated" is true by construction and needs no validation rule. Each slot's
prompt states its index ("this is item 3 of 4") and receives its already-generated siblings as
context so they stay distinct. "Reroll all" on a group rerolls only that group's unlocked slots.

## Implementation

### Backend

All new service files live in `backend/MarkdownHub.Api/Services/AI/`.

**`AiTemplateModels.cs`** — records only (the project's one-type-per-file rule exempts records):

```csharp
public record AiTemplateSlot(string Id, string Name, int Index, int Count);
public record AiTemplateInstruction(string Name, List<string> Rules, string? Format,
                                    string? Example, int? MaxWords, int? MaxSentences);
public record AiTemplateElement(string? LiteralText, AiTemplateSlot? Slot);  // ordered; exactly one set
public record ParsedAiTemplate(List<AiTemplateElement> Elements,
                               Dictionary<string, AiTemplateInstruction> Instructions,
                               List<string> FillInVariables,
                               string Purpose);
public record AiTemplateValidation(bool IsValid, List<string> Problems);
```

**`AiTemplateParser.cs`** — pure static, no I/O, no DI. `Parse(string templateContent)`:

- Extracts and removes the ` ```ai-template ` fenced block; parses it as `Name:` headers followed
  by `- rule` bullets. Recognizes four typed rule prefixes case-insensitively — `Format:`,
  `Example:`, `Max words: N`, `Max sentences: N` — and keeps everything else as free-text rules
  passed verbatim to the model.
- Walks the remaining Markdown, splitting on `{{Name}}` into an ordered element list, using the
  same `\{\{([^}]+)\}\}` shape the frontend already uses so both agree on what a placeholder is.
- Names with an instruction entry become slots (numbered per name); names without become
  `FillInVariables`.
- `Purpose` is the leading heading/prose before the first placeholder, trimmed and capped — used
  as the prompt's "what this document is" layer.
- Caps: 40 slots, 20 distinct names, 8 KB of instruction text. Over-cap returns a validation
  error rather than silently truncating.

**`AiTemplatePromptBuilder.cs`** — builds the user prompt for one slot from the four layers in
design §6, as clearly separated, explicitly labeled blocks:

```
TEMPLATE PURPOSE      (parsed Purpose)
SECTION TO GENERATE   (name, "item 3 of 4", its rules, its Format:)
FORMAT EXAMPLE        (Example:, wrapped in an explicit "style only, never reuse" fence)
ALREADY GENERATED     (other slots' current values; locked ones marked LOCKED)
```

Design §7's failure mode — the model parroting the example's subject — is addressed twice: the
system prompt states that examples demonstrate formatting only, and the example is wrapped inline
with a "do not reuse this example's subject matter" instruction so it survives being far from the
system prompt in a long context.

For `Improve`, the same prompt plus a `CURRENT TEXT` block and an instruction to revise while
keeping the same subject. For a correction retry, plus a `PROBLEM WITH YOUR LAST REPLY` block
naming the failed check.

**`AiTemplateValidator.cs`** — deterministic checks against one slot's generated text.
Deliberately a small fixed set rather than a general rules engine:

- Non-empty after trimming.
- No Markdown headings (`^#{1,6} `) — the AI must never invent structure.
- No leftover `{{...}}` markers.
- No leading preamble line (`Here is…`, `Sure,…`) — strip it when it is followed by a blank line,
  fail when ambiguous.
- `Max words` / `Max sentences` when declared.
- A `Format:` containing `**Name**.` requires the result to start with a bold run followed by a
  period.

Returns `AiTemplateValidation(bool IsValid, List<string> Problems)`.

**`AiTemplateService.cs`** — orchestration for exactly one slot:

1. Build prompt → `IAiService.CompleteAsync(AiPrompts.AiTemplateSystemPrompt, prompt, ct)`.
2. Post-process: trim, strip any code fence the model wrapped the answer in.
3. Validate. On failure, **one** correction retry with the problems fed back.
4. If it still fails, return the content anyway with `Warnings` populated — per design §12 a
   failed validation must never blank out a result. The UI shows a warning badge; the user can
   reroll or edit.
5. `AiServiceException` propagates untouched to the controller; the caller's existing slot value
   is never modified (the client only overwrites a slot on a 200).

#### `Controllers/AI/AiTemplateController.cs`

| Route | Verb | Purpose |
|---|---|---|
| `/api/ai/template/parse` | POST | Permission-check + read template, return parsed elements/slots/instruction summaries/fill-in variables. **No AI call**, so it works and gives a useful error even with Ollama down. |
| `/api/ai/template/generate` | POST | Generate / reroll / improve **one** slot. |

`GenerateRequest(string TemplatePath, string SlotId, string Mode, List<SlotValue> Slots)` where
`SlotValue(string Id, string Content, bool Locked)` and `Mode` parses to a
`Generate | Improve` enum via `Enum.TryParse(..., ignoreCase: true, ...)`, matching how
`AiAssistantController` handles `AssistantAction`.

The controller re-reads and re-parses the template server-side on every call rather than trusting
a client-supplied structure — the client can only *name* a slot, never inject instructions.

"Generate all" is N sequential client calls, not a server loop. That gives per-slot progress, lets
the user stop partway, keeps each request inside the existing Ollama timeout, and means a failure
on slot 5 leaves slots 1–4 intact.

Reuse the existing `GET /api/ai/assistant/status` for the "Ollama not found" upfront state — no
new status endpoint.

#### `Controllers/AI/AiTemplateModels.cs`

Request/response records, following `AiAssistantModels.cs`.

#### Modified backend files

- **`Services/AI/AiPrompts.cs`** — add `AiTemplateSystemPrompt` (design §6 layer 1: fill only the
  requested section, add nothing unrequested, preserve Markdown, never copy examples, no preamble
  or explanation, no headings).
- **`Program.cs`** — `builder.Services.AddScoped<AiTemplateService>();` next to the existing
  `AddScoped<IAiService, OllamaAiService>()` at line 41. The parser, prompt builder, and validator
  are static and need no registration.

`FilesController`/`TemplateInfo` are deliberately **not** touched: the frontend discovers whether
a template is an AI Template from the parse call, which returns zero slots for an ordinary one.

### Frontend

**`components/AiTemplatePanel.tsx`** (new) — a modal using the existing `.modal` /
`.modal-wide` / `.modal-overlay` pattern (`styles/index.css:321-355`), launched from `FileTree`'s
create-from-template flow. State per slot:

```ts
interface SlotState {
  id: string; name: string; index: number; count: number;
  content: string; locked: boolean; busy: boolean;
  warnings: string[]; error: string | null;
}
```

Layout mirrors design §9: slots grouped by name; a group header with **Reroll all** when
`count > 1`; each card showing the text plus **Reroll**, **Improve**, **Lock/Unlock**, and inline
edit — reusing the `ai-result-card` / `ai-result-card-textarea` visual language from
`AiAssistantPanel` (`styles/index.css:617-631`). A footer with **Generate all**, the page name,
the target folder, and **Save as page**.

Behavior rules that fall straight out of the design:

- A locked slot is skipped by Generate all / Reroll all and is sent to the backend flagged
  `locked: true` so it becomes context (design §10).
- A slot's content is replaced only on a successful response; a failure sets `error` on that card
  and leaves the previous value intact (design §12, principle #7).
- Generate all runs sequentially so each slot sees the previous ones (design §8), with a stop
  control.
- If `getAiAssistantStatus()` reports unavailable, the panel opens with the same upfront
  "Ollama installation not found" message `AiAssistantPanel` already shows, rather than failing
  per slot.

**`components/aiTemplate.ts`** (new) — pure helpers, unit-testable without React:
`assembleDocument(elements, slots, variables)` joins literal segments with slot values and
substitutes any fill-in variables; `groupSlots(slots)` groups by name for rendering.

**Modified frontend files**

- `api/client.ts` — `AiTemplateParseResult`, `AiTemplateSlotValue`, `AiTemplateGenerateResult`
  interfaces plus `aiTemplateParse()` / `aiTemplateGenerate()`, following the existing
  `request<T>` pattern used by `aiAssistant` (lines 371-377).
- `components/FileTree.tsx` — in `commitCreate` (line 253), after `api.getPage(templateRelativePath)`
  succeeds, call `aiTemplateParse`; if it returns slots, open `AiTemplatePanel` (collecting any
  fill-in variables through the existing `TemplateVariablesModal` first), otherwise fall through to
  today's exact behavior. On save, route through the existing `createPage(folder, name, content)`
  so tree reload and error handling are unchanged.
- `styles/index.css` — `.ai-template-*` classes, reusing the existing card and modal language
  rather than inventing a new one.

### Database

**No changes.** No new tables, columns, entities, or `DatabaseMigrations.cs` statements. AI
Templates are ordinary pages already flagged `IsTemplate`; sessions are ephemeral; results are
ordinary saved documents that pick up version history, search indexing, and backlinks through the
existing save path.

### Other

- **Documentation** — `README.md`: an AI Templates section with the ` ```ai-template ` authoring
  syntax, the recognized rule prefixes, and a full worked example. `CLAUDE.md`: an "AI Templates
  Design" entry under the task list plus a bullet in "What's implemented".
- **Configuration** — none. Reuses `Ai:Ollama:*` and the admin model override. Worth a README
  note that a template with many slots means many sequential model calls, and that
  `Ai:Ollama:TimeoutSeconds` applies per slot, not per document.

## Files to Modify

| File | Changes |
|------|---------|
| `backend/MarkdownHub.Api/Services/AI/AiPrompts.cs` | Add `AiTemplateSystemPrompt` |
| `backend/MarkdownHub.Api/Program.cs` | Register `AiTemplateService` |
| `frontend/src/api/client.ts` | AI Template types + `aiTemplateParse` / `aiTemplateGenerate` |
| `frontend/src/components/FileTree.tsx` | Route an AI Template to `AiTemplatePanel`; pass fill-in variables through |
| `frontend/src/styles/index.css` | `.ai-template-*` styles |
| `README.md` | AI Templates usage + authoring syntax |
| `CLAUDE.md` | Feature entry and implemented-list bullet |

## Files to Create

| File | Purpose |
|------|---------|
| `backend/MarkdownHub.Api/Services/AI/AiTemplateModels.cs` | Parsed-template + validation records |
| `backend/MarkdownHub.Api/Services/AI/AiTemplateParser.cs` | Structure + instruction-block parsing |
| `backend/MarkdownHub.Api/Services/AI/AiTemplatePromptBuilder.cs` | Layered prompt construction |
| `backend/MarkdownHub.Api/Services/AI/AiTemplateValidator.cs` | Deterministic per-slot checks |
| `backend/MarkdownHub.Api/Services/AI/AiTemplateService.cs` | Generate → validate → correct orchestration |
| `backend/MarkdownHub.Api/Controllers/AI/AiTemplateController.cs` | `/api/ai/template/parse`, `/api/ai/template/generate` |
| `backend/MarkdownHub.Api/Controllers/AI/AiTemplateModels.cs` | Request/response records |
| `backend/MarkdownHub.Api.Tests/Services/AiTemplateParserTests.cs` | Parser coverage |
| `backend/MarkdownHub.Api.Tests/Services/AiTemplateValidatorTests.cs` | Validation coverage |
| `backend/MarkdownHub.Api.Tests/Services/AiTemplateServiceTests.cs` | Retry/fallback coverage with a fake `IAiService` |
| `backend/MarkdownHub.Api.Tests/Controllers/AiTemplateControllerTests.cs` | Auth, permissions, error mapping |
| `frontend/src/components/AiTemplatePanel.tsx` | Review/iterate UI |
| `frontend/src/components/aiTemplate.ts` | `assembleDocument` / `groupSlots` |
| `frontend/src/components/aiTemplate.test.ts` | Assembly coverage |
| `frontend/src/components/AiTemplatePanel.test.tsx` | Panel behavior coverage |

## Data Flow

**Initial generation**

1. User picks a template in the file tree's create-from-template dropdown and types a page name.
2. Frontend loads the template (existing `api.getPage`) then `POST /api/ai/template/parse
   { templatePath }`.
3. Backend: View permission check → `MarkdownFileService.ReadAsync` → `AiTemplateParser.Parse` →
   returns ordered elements, slots, instruction summaries, and fill-in variable names.
4. Zero slots → existing flow, done. Otherwise any fill-in variables are collected via the
   existing `TemplateVariablesModal`, then `AiTemplatePanel` opens with empty slots.
5. **Generate all** loops the slots in document order. For each unlocked slot:
   `POST /api/ai/template/generate { templatePath, slotId, mode: "Generate", slots }`, where
   `slots` carries every slot's current content and locked flag.
6. Backend re-reads and re-parses the template, builds the layered prompt including
   already-generated and locked siblings, calls `IAiService.CompleteAsync`, post-processes,
   validates, retries once on failure, returns `{ content, warnings }`.
7. Panel writes the returned content into that slot and moves to the next.

**Reroll / Improve** — identical to step 5 for a single slot, with `mode` set accordingly. Every
other slot is unchanged and travels along as context.

**Save** — `assembleDocument` joins literal segments with slot values, then the existing
`createPage` → `api.savePage` path runs. From that moment the result is an ordinary document:
versioned, indexed, backlinked, editable.

## Testing

**Backend (xUnit, existing conventions)**

- `AiTemplateParserTests` — single and repeated placeholders; slot numbering and counts;
  instruction block extracted and removed from the output structure; typed rules (`Format:`,
  `Example:`, `Max words:`) parsed; a name without an instruction becomes a fill-in variable, not
  a slot; a template with no instruction block yields zero slots; cap violations rejected.
- `AiTemplateValidatorTests` — heading rejected; leftover `{{...}}` rejected; word/sentence
  limits; the `**Name**.` format rule; preamble stripping; valid content passes untouched.
- `AiTemplateServiceTests` — with a fake `IAiService` (same pattern as
  `AiAssistantControllerTests`' fake): invalid-then-valid triggers exactly one retry;
  invalid-twice returns the content plus warnings rather than throwing; the correction prompt
  names the failed check; locked slots appear in the prompt marked as locked.
- `AiTemplateControllerTests` — unauthenticated → 401; template the user can't View → 403;
  missing template → 404; unknown slot id → 400; `AiServiceException` → 502 with its message.

**Frontend (Vitest + Testing Library)**

- `aiTemplate.test.ts` — assembly preserves literal Markdown exactly, substitutes slots in order,
  handles repeated placeholders and empty slots.
- `AiTemplatePanel.test.tsx` — a locked slot is skipped by Generate all and sent flagged locked;
  a failed reroll preserves the previous content and shows an error on that card only; warnings
  render; Save calls through with the assembled document.
- Regression check that an ordinary (non-AI) template still goes through
  `TemplateVariablesModal` unchanged.

**Manual verification**

Author the Adventure template from design §4, run Generate all against a real Ollama, confirm four
distinct interactibles in the requested `**Name**.` format, lock the encounter, reroll the scene,
and confirm the saved page opens and edits normally.

## Risks / Considerations

- **Latency.** Per-slot generation means a 7-slot template is 7 sequential local-model calls —
  potentially a minute or more on a 20b model. This is the deliberate cost of deterministic
  structure. Mitigated by per-slot progress, usable partial results, and a stop control, but it is
  the main thing that could feel slow. (Future optimization: generate context-independent slots in
  parallel.)
- **Existing `{{Variable}}` behavior.** `FileTree.tsx` prompts for *every* `{{Name}}` today. The
  change must be surgical — a template with no instruction block has to behave exactly as it does
  now, or existing user templates regress. Covered by an explicit test.
- **Small local models and instruction adherence.** Validation catches format violations, but a
  model that ignores "be concise" produces valid-but-poor output. The Improve/Reroll loop is the
  answer; expect prompt-wording iteration after real use.
- **Example leakage** (design §7) is the highest-risk *quality* failure. Addressed in two places
  (system prompt + inline fencing), but worth checking explicitly during manual verification.
- **Ollama unavailable.** `/parse` needs no AI by design, so the panel can open and explain the
  situation rather than failing opaquely; the existing assistant-status check supplies the message.
- **No session persistence.** Closing the modal loses in-progress generation. Acceptable for a
  first version (design §3); `sessionStorage` would be a cheap follow-up.
- **Template re-read on every slot call.** Re-reading a small file per slot is negligible and buys
  the security property that clients cannot inject instructions.

## Open Questions

1. **Entry point for v1.** Recommended: the existing file-tree "New page from template" flow only.
   Should AI Templates *also* be reachable from the editor toolbar, to generate content into an
   already-open page (the way the assistant's "Add to page" works)? Easy to add later, but it
   changes the panel's save path, so it's worth deciding now if you want it.
2. **Feature name in the UI.** The design calls it "AI Templates"; this plan uses that throughout.
   The earlier draft called it "Content Forge". Confirm which name should appear in the UI, README,
   and route names before implementation starts, since it's baked into file and endpoint names.

Two items from the earlier draft are settled rather than left open: the instruction format is the
fenced ` ```ai-template ` block (no frontmatter parser exists in this app), and generation is
slot-at-a-time (one-shot generate-then-split would reintroduce exactly the structural failures
§11 and §12 exist to prevent).

## Implementation Order

1. `AiTemplateParser` + records, with tests. Everything else depends on the parsed shape.
2. `AiTemplateValidator`, with tests.
3. `AiTemplatePromptBuilder` + `AiPrompts.AiTemplateSystemPrompt`.
4. `AiTemplateService` (generate → validate → retry → warnings), with fake-`IAiService` tests.
5. `AiTemplateController` + models + DI registration, with controller tests. The backend is now
   independently exercisable via curl before any UI exists.
6. `aiTemplate.ts` assembly helpers, with tests.
7. `api/client.ts` types and calls.
8. `AiTemplatePanel.tsx` — parse/display first, then Generate all, then Reroll/Improve/Lock, then
   Save.
9. `FileTree.tsx` wiring, keeping the non-AI path unchanged.
10. Styles.
11. Manual end-to-end run against real Ollama with the design §4 Adventure template.
12. `README.md` + `CLAUDE.md`.
