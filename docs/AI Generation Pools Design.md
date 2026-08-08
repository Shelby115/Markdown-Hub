# AI Generation Pools

## The problem

AI Templates generate one placeholder per model call. On a self-hosted Ollama that is slow
enough to be felt — a template with six placeholders is six sequential waits, every time.

But most of that work doesn't actually depend on *when* it's asked for. An interactible, an NPC
name, a rumour: these are self-contained, and one is as good as another. The only reason they're
generated on demand is that nothing generated them earlier.

## The idea

A **pool** is a named library of pre-generated content for one kind of placeholder. A background
service fills it during idle time; a template that opts in gets an entry out of the database
instead of waiting on the model.

Not everything belongs in a pool. A pool entry is written with no knowledge of the page it will
land in, so anything that has to agree with its surroundings (a summary of the scene above it, an
encounter matching the biome that was just generated) stays an ordinary placeholder. Pools are for
the independent parts.

## How a template uses one

One line inside the existing instruction block:

````markdown
```ai-template
Interactible:
- Pool: Interactible
```
````

Explicit opt-in rather than matching pool names against placeholder names automatically: pool names
are global, and two unrelated templates both using `{{Description}}` shouldn't silently share a
library. The `Pool:` prefix is parsed the same way `Format:`, `Example:`, `Max words:` and
`Max sentences:` already are, so there's no new authoring surface.

When a slot is pool-backed, the **pool's** prompt is authoritative — the pool is where that content
is defined, and the template merely references it. A pooled entry and a live fallback then produce
the same kind of thing.

## Lifecycle of an entry

Three states, one column:

- **Ready** — available. Only Ready entries are ever handed out.
- **Used** — already given to someone. Never served again, so no two pages get the same
  interactible.
- **Forgotten** — a user rejected it.

Rows are kept after they leave Ready rather than deleted, because each row carries a SHA-256 of its
normalized content and a unique `(PoolId, ContentHash)` index. That hash is the whole mechanism
behind "forget this forever": delete the row and the generator is free to produce the same text
again tomorrow. It also stops the pool filling with near-identical entries.

Used entries are retention-bounded (90 days by default) since their only job is dedupe memory.
Forgotten entries are never cleaned up — that's what makes forgetting permanent.

A pool miss falls back to a live generation, recorded as Used for the same reason. An unknown pool
name (typo, deleted pool) degrades to ordinary template generation rather than failing.

## The background generator

`PoolFillHostedService` ticks on a configurable interval and adds **at most one entry per pool per
tick**. It's meant to use idle time, not to compete with someone who is actually editing, and the
one-at-a-time pace makes that true without any concurrency machinery.

Each tick re-reads its settings, so pausing or rescheduling takes effect immediately:

| Setting | Default | What it does |
|---|---|---|
| Paused | off | Hard stop, nothing generates |
| Allowed window (UTC) | none | Only generate between these times; wraps past midnight |
| Interval | 60s | Seconds between passes |
| Used-entry retention | 90 days | How long dedupe memory is kept |
| Per-pool: enabled | **off** | Nothing runs against Ollama until an admin asks |
| Per-pool: target count | 20 | Ready entries to keep on hand; also the cap |

Generated entries go through the same `AiTemplateValidator` a live slot does, with the same single
correction retry — but unlike a live generation, a still-failing result is dropped rather than
returned with warnings. Nobody is waiting on it, so a bad entry never has to enter the pool.

Variety comes from showing the model the pool's most recent entries and telling it not to repeat
them; without that, a fixed prompt produces the same handful of ideas over and over.

## What this deliberately doesn't do

- **No parallel generation.** One local model, one request at a time.
- **No per-user pools or per-user forgetting.** Self-hosted, small team; a global library is the
  simpler and more useful thing.
- **No pool sharing across placeholder context.** If an entry needs to know about the rest of the
  page, it shouldn't be pooled.

## Where it lives

| Piece | File |
|---|---|
| Entities | `Data/Entities/GenerationPool.cs`, `GenerationPoolEntry.cs`, `GenerationPoolEntryStatus.cs` |
| Pool + settings logic | `Services/AI/GenerationPoolService.cs`, `GenerationPoolSettings.cs` |
| Background filler | `Services/AI/PoolFillHostedService.cs` |
| Pool-aware slot generation | `Services/AI/AiTemplateService.cs` |
| Pool prompt | `Services/AI/AiTemplatePromptBuilder.BuildForPool` |
| Endpoints | `Controllers/AI/AiPoolController.cs` (forget), `AiPoolAdminController.cs` (management) |
| Admin UI | `frontend/src/components/AiPoolAdmin.tsx` |
| Forget button | `frontend/src/components/AiTemplatePanel.tsx` |
| Retention cleanup | `Services/Admin/HistoryCleanupHostedService.cs` |
