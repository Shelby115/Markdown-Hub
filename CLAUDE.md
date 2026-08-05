# Markdown Hub — self-hosted, self-referenced, self-owned Markdown web app

A self-hosted, web-based Markdown hub: an ASP.NET Web API backend keeps your
`.md` files on the server filesystem as the source of truth, while a React SPA
gives you a live-preview, wiki-style browser editing experience (CodeMirror 6)
— replacing the need for a paid notes-sync/publish subscription.

## Stack

| Layer          | Technology                                             |
|----------------|---------------------------------------------------------|
| API            | ASP.NET Web API, .NET 10                                |
| Frontend       | React + TypeScript + Vite + CodeMirror 6                |
| Database       | SQLite (metadata, permissions, search index, audit log)  |
| Auth           | OpenID Connect / JWT bearer, multiple providers, admin-configurable |
| Search         | SQLite FTS5 virtual table                                |
| Orchestration  | Docker Compose                                            |

## Project layout

```
backend/MarkdownHub.Api/
  Controllers/     REST endpoints (files, attachments, search, backlinks, publish, admin, health)
  Data/            EF Core DbContext + entities (metadata only — never file content)
  Services/        Hub path safety, permissions, Markdown I/O, rendering, search,
                    file-watching, backups
  Middleware/       Admin authorization policy handler
frontend/src/
  auth/            Generic OIDC client (oidc-client-ts) - fetches enabled providers from the
                    API at runtime, so the built frontend image isn't tied to any one provider
  api/             Typed fetch client
  components/       FileTree, SearchBar, Editor (CodeMirror), liveMarkdown (decorations), Backlinks
  pages/            Route-level views (PageView, PublishedPage, Welcome, AuthCallback)
keycloak/
  markdown-hub-client-import.json  Example client config to import into a Keycloak realm - one
                                    possible OIDC provider among any number the admin page supports
docker-compose.yml
docker-compose.override.yml  (gitignored; optional, host-specific overrides - see below)
```

## Running it

This app doesn't bundle an identity provider - it validates tokens from whichever OIDC
provider(s) you configure and enable via the admin page ("OIDC providers" section). Any
standards-compliant OIDC provider works (Keycloak, Authentik, Zitadel, Auth0, ...) as long as
it can act as a public SPA client with authorization-code + PKCE and no client secret. See
[SetupGuide.md](SetupGuide.md) for a full first-time-setup walkthrough, including gotchas.

```bash
cp .env.example .env      # fill in OIDC_DEFAULT_* (see comments in .env.example) and HUB_HOST_PATH
docker compose up --build
```

`OIDC_DEFAULT_*` only matters on a brand-new database - it seeds the very first provider so
there's something to log in with. After that, providers live in the database and are managed
entirely through the admin page; changing `OIDC_DEFAULT_*` later has no effect. An example
Keycloak client you can import to stand up that first provider is in
`keycloak/markdown-hub-client-import.json` (public client, PKCE required, "Client
authentication" OFF, with an audience mapper adding the client's own ID to `aud` — see that
file's fields for the exact settings). Whatever provider you use, its client needs
`<your origin>/auth/callback` allowed as a redirect URI.

- Frontend: http://localhost:8086
- API: http://localhost:8085 (health check at `/health`)

The **first user ever to log into the app** is automatically promoted to application
administrator, independent of provider roles — admin status here is an app-level concept, not
something any provider grants, since folder permissions are managed entirely inside this app's
database. If you'd rather guarantee a specific account gets admin instead of relying on "first
one in," set `ADMIN_USERNAME` in `.env` to that account's username ahead of their first login.

If your identity provider and this app run on the same machine but your router doesn't support
NAT hairpin/loopback (common on home networks), you'll need a Docker `extra_hosts` entry
mapping the provider's public hostname to `host-gateway` for the `api` service. This is
host-specific, so it doesn't live in the shared `docker-compose.yml` — add a
`docker-compose.override.yml` next to it instead (Compose merges it automatically, and it's
gitignored):

```yaml
services:
  api:
    extra_hosts:
      - "your-idp-hostname.example.com:host-gateway"
```

For local frontend development without Docker: `cd frontend && npm install && npm run dev`
(proxies `/api` to `http://localhost:8085` if you adjust `vite.config.ts` accordingly —
run the API separately, e.g. `dotnet run` from `backend/MarkdownHub.Api`).

### Running tests

- Backend: `cd backend/MarkdownHub.Api.Tests && dotnet test` (xUnit, EF Core InMemory provider,
  no real database or Docker needed).
- Frontend: `cd frontend && npm test` (Vitest + Testing Library, jsdom environment).

## What's implemented

- OIDC login (authorization code + PKCE) on the SPA against any number of admin-configured
  providers; JWT bearer validation on the API resolved dynamically per-issuer, so more than
  one provider can be enabled at once (`OidcProvidersController`, `Services/OidcProviderValidationService.cs`).
  The SPA fetches the enabled-provider list from the API at runtime (`/api/auth/providers`)
  rather than baking one in at build time, auto-signing in against the sole provider when
  there's only one, or showing a picker when there's more than one.
- `ADMIN_USERNAME` config seeds a pending administrator account on startup (`StartupSeeder`),
  so a specific account can be guaranteed admin on first login instead of relying on "whoever
  logs in first."
- Path-traversal-safe filesystem access (`HubPathService`) used by every file operation
- Markdown CRUD, folder tree with inline rename/delete/create — all permission-checked per folder
- **Live-preview editor** (CodeMirror 6): markdown renders styled by default; raw syntax for
  a given element only reveals when the cursor is genuinely on/adjacent to *that element*
  (not just anywhere on its line). Covers headings, bold/italic, inline code, blockquotes,
  and GFM tables (rendered as real `<table>` elements, editable as raw source when focused).
- Wiki-style `[[wiki links]]`, `[[Page|alias]]`, `![[image/audio/video/PDF embeds]]`, and
  `![[note transclusion]]` (embedding another page's full rendered content inline) - all
  resolved hub-wide by filename, not just by exact relative path, the same way other
  wiki-style note apps resolve links. Links to non-existent pages render distinctly (red/dashed).
- **Audio, video, and PDF embeds** (`![[song.mp3]]`, `![[clip.mp4]]`, `![[Handbook.pdf]]`),
  recognized by extension alongside the existing image/note-transclusion embed handling.
  In the live editor, each renders as a real inline widget - `<audio controls>`,
  `<video controls>`, and (for PDFs) an `<iframe>` preview - built directly as DOM elements by
  `liveMarkdown.ts` (`AudioEmbedWidget`/`VideoEmbedWidget`/`PdfEmbedWidget`), the same way image
  embeds already worked, fetching the file as a blob through the existing authenticated
  `/api/attachments` endpoint. Server-rendered HTML (used by note transclusion and published
  pages, `MarkdownRenderService`) renders real `<audio>`/`<video>` tags too, but PDFs render as
  a plain link there instead of an inline viewer - deliberately keeps the HTML sanitizer's
  allowed-tag list free of `iframe`/`embed`/`object`, which (unlike `audio`/`video`/`img`) can
  render arbitrary same-styled foreign content if a raw tag ever ended up in page source.
  `PublishController`'s anonymous attachment endpoint and `AttachmentsController` both serve
  the added extensions with correct content types.
- External `[text](url)` links open in a new tab, visually distinct from wiki-links (↗ suffix).
  Wiki-links inside table cells are also clickable.
- Auto-save (2s debounce after typing stops) with concurrent-edit conflict detection
  (mtime-based) that saves a `.conflict.<timestamp>.md` copy instead of ever silently
  overwriting.
- Markdown → sanitized HTML rendering (Markdig + HtmlSanitizer) — no raw HTML ever reaches
  the browser; used for note transclusion and the public/published page view.
- SQLite FTS5 search across page names, folders, and content, filtered to what the
  requesting user can see, with snippet highlighting. A startup reconciliation pass indexes
  any content already on disk (e.g. a freshly bind-mounted existing hub), not just live
  changes made through the app.
- Backlinks graph, kept in sync on every save
- Folder-level permissions (View / Edit / Manage) with prefix-based inheritance
- **Publishing**: per-page publish/unpublish toggle in the editor toolbar, unguessable
  slugs, no filesystem path ever exposed, unauthenticated read-only view at `/published/:slug`
  (a separate route that bypasses auth entirely, by design)
- Filesystem watcher that reindexes on external changes (git pulls, other editors, etc.)
  after the initial startup reconciliation
- Image paste-to-upload in the editor, with extension + magic-byte validation
- Zip backups (Markdown + DB + config + attachments), manual trigger + daily schedule + retention
- `/health` endpoint checking app, hub directory, DB, and OIDC provider reachability
- Admin endpoints to rebuild the search index and file metadata from the filesystem independently
- React error boundary surfacing crash details on screen (rather than a blank page) for
  easier diagnosis if something in the editor does throw
- Per-user default/home folder: any user can mark a folder as home from that folder row's
  "⋮" menu in the file tree (shown as a small ⌂ badge next to the folder name once set); the
  tree auto-expands to it (and its ancestors) whenever they land on the home page. Self-service
  (`PUT /api/me/default-folder`), not admin-gated.
- New folders: a "New folder" button lives in the same "⋮" menu (root header and every folder
  row) alongside "New page from template," keeping each row down to a single quick "+" (new
  file) action plus the menu for less-frequent actions. Backend: `POST /api/files/folder/{path}`,
  gated by the same Edit-level folder permission as creating a file there.
- Folder rename/move: also in the "⋮" menu. `POST /api/files/rename-folder/{path}` moves the
  directory on disk and bulk-rewrites every contained document's `RelativePath` (recursively,
  including soft-deleted ones) so each keeps its stable ID and version/activity history, and
  updates any `FolderPermission` grants pointing at the folder or nested inside it so access
  isn't silently lost. Gated by Manage on the source folder (same bar as deleting) and Edit on
  the destination (same bar as file rename); refuses moving a folder into itself.
- Folder deletion: "Delete folder" in the "⋮" menu, gated by the same confirmation dialog
  pattern as file delete but warning explicitly that every file and subfolder inside will be
  deleted too. `DELETE /api/files/folder/{path}` (`FilesController.DeleteFolder`) removes the
  directory from disk and soft-deletes every contained document (recursively) the same way a
  single file delete does, so version/activity history for each is preserved and individually
  recoverable during the retention window even though the folder itself is gone. Gated by
  Manage on the folder, same bar as deleting a single file.
- Undo/redo: CodeMirror's built-in history was already active via `basicSetup`, so
  Ctrl+Z/Ctrl+Shift+Z already worked - added explicit toolbar buttons for discoverability,
  reflecting live undo/redo depth.
- Inline dice roller (see the Dice Roller Design section) and AI-assisted editing / AI
  knowledge assistant panel built on a shared `IAiService`/Ollama integration (see the AI
  sections below and `Knowledge-Assistant-Design.md`). The assistant panel checks
  `GET /api/ai/assistant/status` once on load and shows an upfront "Ollama installation not
  found" message (pointing at the `OLLAMA_BASE_URL`/`OLLAMA_MODEL` env vars) instead of the
  full panel when Ollama isn't reachable or has no model installed, rather than only failing
  once someone tries to use it.
- Version history and activity log (see the Version History and Activity Log Design section
  and `Activity-And-History.md`): coalesced auto-versioning with a GitHub-style diff/compare/
  restore UI per document, soft-deleted-document recovery, and an admin-only, filterable/
  paginated Activity Log covering auth, file/folder, and settings/permission events.

All items from the original task list have shipped an initial version - see
`Knowledge-Assistant-Design.md` for what's deliberately deferred beyond the knowledge
assistant's first pass (folder-as-context, RAG/semantic search, external web research,
additional card types, "Add as New Page", persisted conversation history).

## Deliberately scoped out for now

- **Cron scheduling.** `ScheduledBackupHostedService` runs backups daily at 03:00 UTC as a
  placeholder; swap in a small cron library (e.g. Cronos) to honor the configurable
  `BackupSchedule` setting for arbitrary schedules.
- **Note-embed and image-embed rendering inside table cells.** Wiki *links* in table cells
  are clickable; embeds in cells currently just show the plain target name as text.

## Security notes

- Every filesystem path derived from user input goes through
  `HubPathService.ResolveSafe`, which rejects anything that would resolve
  outside `MarkdownRoot`.
- All rendered Markdown is sanitized (`Ganss.Xss` namespace, `HtmlSanitizer` NuGet package)
  after `Markdig` conversion — this is the single choke point, so don't add another
  raw-HTML render path. Note transclusion reuses this same sanitized output.
- Uploaded attachments are checked against an extension allow-list *and* magic
  bytes, so a renamed executable can't pass as an image.
- Production mode disables the developer exception page so stack traces/paths
  never reach the browser; `/error` returns a generic message instead.
- `<audio>`/`<video>`/`<iframe>` src attributes can't attach an `Authorization` header, so audio/
  video/PDF embeds in the live editor authenticate via an `access_token` query param instead
  (`Program.cs`'s `OnMessageReceived`, `api.attachmentStreamUrl`) - the same pattern ASP.NET
  Core's own docs recommend for SignalR for the same reason. Deliberately scoped to exactly
  `/api/attachments` (never the whole API) to keep the token's extra exposure surface (browser
  history, server access logs) as narrow as possible; the endpoint still runs the same
  permission check either way. This replaced an earlier version that fetched the whole file as
  a Blob via JS first (still how image embeds work) - fine for small images, but it forced
  large audio/video files to download and sit fully decoded in memory before playback could
  even start, which is what caused heavy lag and crashes. `PhysicalFile(..., enableRangeProcessing: true)`
  on both the authenticated and published-page attachment endpoints lets the browser's native
  media engine request only the bytes it needs instead.
- The `/published/:slug` route is intentionally unauthenticated and only ever resolves
  wiki-links within a published page to *other published pages* — it never exposes
  filesystem paths or the existence of unpublished content.
  
## To Do list

Nothing outstanding - the last remaining items (folder deletion, the History dialog's button
label, and audio/video/PDF embeds) shipped; see "What's implemented" above.

### AI-Assisted Editing Design

* [x] **Add self-hosted AI functionality using Ollama** — `IAiService`
  (`backend/MarkdownHub.Api/Services/IAiService.cs`) is the provider-independent abstraction;
  `OllamaAiService` is the only implementation, calling a local Ollama instance's `/api/chat`.
  Configured under `Ai:Ollama:{BaseUrl,Model,TimeoutSeconds}` (see `appsettings.json` /
  `.env.example` - `OLLAMA_BASE_URL` defaults to `http://host.docker.internal:11434`,
  `OLLAMA_MODEL` to `gpt-oss:20b`, both overridable per your actual Ollama setup). The frontend
  never talks to Ollama directly - only through the .NET API. System prompts are centralized in
  `Services/AiPrompts.cs`. All AI interaction now happens through the always-available AI
  Assistant side panel (see below) rather than a toolbar button - the original editor-toolbar
  **AI** button (Summarize / Improve Writing / Fix Grammar on the current selection or whole
  page, via `POST /api/ai/edit`) was removed once the panel covered the same ground; the
  `/api/ai/edit` endpoint and `AiController` are still present on the backend but currently
  unused by the frontend.
* [x] **AI model selection.** Admins can pick which installed Ollama model the whole app uses
  from the Admin page's "AI model" section - a text input with autocomplete suggestions pulled
  live from Ollama's `/api/tags` (falls back to manual entry if Ollama's unreachable). The
  choice is a single app-wide setting (`AppSettings` table, key `Ai.Ollama.Model`), applies to
  every user immediately with no restart, and takes precedence over the `OLLAMA_MODEL`
  config/env default; "Reset to default" clears the override. `IAiService.ListModelsAsync`
  keeps this provider-independent the same way `CompleteAsync` is.

  * Add an AI service abstraction (`IAiService`) to the .NET 10 backend so the application is not tightly coupled to Ollama.
  * Implement an Ollama-backed provider using the existing local Ollama installation.
  * Do **not** expose Ollama directly to the browser; all AI requests should go through the .NET backend.
  * Inspect the existing application architecture, editor implementation, authentication, and deployment configuration before implementing this feature. Follow the application's existing patterns rather than introducing unnecessary new architecture.
  * Add an **AI** menu/action to the markdown editor that operates on either:

    * The currently selected text, or
    * The entire current page when no text is selected.
  * Initial predefined actions:

    * **Summarize** — produce a concise summary of the supplied text.
    * **Improve Writing** — rewrite the supplied text for clarity and readability while preserving its meaning.
    * **Fix Grammar** — correct spelling, grammar, and punctuation without unnecessarily changing the author's wording.
  * The backend should receive the selected/page content and the requested action, construct the appropriate system/user prompts, send the request to Ollama, and return the result to the frontend.
  * Display the generated response in an appropriate UI component without immediately overwriting the user's content.
  * Allow the user to review the generated result and choose whether to insert/replace the selected text with it.
  * Handle loading states, Ollama connection failures, timeouts, and other errors gracefully.
  * Keep prompts centralized and easy to modify/add later.
  * Make the Ollama model configurable through application configuration rather than hard-coding it in the UI or individual actions.
  * Do not implement RAG, embeddings, vector databases, autonomous agents, or knowledge-base-wide context as part of this task. Those can be added later.
  * Add appropriate tests for the AI service and API behavior where consistent with the existing project's testing approach.
  * Update the README/documentation with any required configuration settings and instructions for running the feature locally.
  * Include the ability to undo/redo this change.
  
  
### Dice Roller Design

* [x] **Add an inline dice roller to the markdown editor** — implemented in the live-preview
  editor (`frontend/src/components/diceRoller.ts` for parsing/rolling,
  `liveMarkdown.ts`'s `DiceRollWidget` for the clickable button). Notation like `2d20+1` or
  `+d20` renders as a `🎲 2d20+1` button; clicking rolls via `crypto.getRandomValues` (rejection
  sampling, no modulo bias) and shows each die, the modifier, and the total inline, re-rollable
  without a page reload. Dice count is capped at 100, sides at 1000. The raw notation is never
  rewritten in the saved file - only rendered as a widget. Scoped to the live editor for now;
  dice notation inside server-rendered HTML (note transclusion bodies, published pages) still
  renders as plain text rather than a clickable button, since those are static sanitized HTML
  with no client-side hydration - a natural follow-up if wanted.

  * Detect dice notation in rendered markdown using the syntax `NdS`, `NdS+X`, or `NdS-X`.

    * Examples: `d20`, `2d20`, `2d20+1`, `4d6-2`.
    * `N` is the number of dice.
    * `S` is the number of sides per die.
    * `X` is an optional flat modifier.
  * Convert recognized dice notation into a clickable dice-roll UI/button when the page is rendered.
  * Clicking the dice notation should roll the specified dice and display:

    * The individual result of every die.
    * The modifier, if present.
    * The final total.
  * Support **advantage/disadvantage** using a `+` or `-` prefix:

    * `+1d20` / `+d20` = roll twice and use the higher result.
    * `-1d20` / `-d20` = roll twice and use the lower result.
    * For advantage/disadvantage, display both rolls and clearly indicate which result was selected.
    * The prefix should apply to the dice roll, not the flat modifier. For example, `+1d20+5` means roll `1d20+5` twice and take the higher result.
  * Support combinations such as:

    * `d20`
    * `2d20`
    * `2d20+1`
    * `4d6-2`
    * `+d20`
    * `+1d20+5`
    * `-d20`
    * `-2d20+3`
  * Do not interpret arbitrary mathematical expressions as dice notation.
  * Define sensible validation limits for the number of dice and sides to prevent accidental or abusive rolls.
  * Rolling should use a suitable random number generator available to the application/platform rather than predictable pseudo-random behavior.
  * The roller should be usable repeatedly without requiring the page to be reloaded.
  * Preserve the original markdown source. Dice notation should remain ordinary text in the editor/database; the interactive roller should be generated only when the content is rendered.
  * Follow the existing frontend/editor architecture and styling conventions rather than introducing an unrelated UI framework.
  * Add tests covering parsing, modifiers, advantage/disadvantage, totals, invalid notation, and edge cases.
  * Update the README/documentation with the supported dice notation and examples.

### Version History and Activity Log Design

Full design doc: `Activity-And-History.md`. Implements both features on a shared backbone
rather than as unrelated systems, per that doc's section 3.

* [x] **Stable document identity.** `PageMetadata.Id` (already existed, independent of
  `RelativePath`) is the "logical document ID" version/activity history is keyed on. Two bugs
  that would have silently broken this were fixed as part of this work: deleting a page used to
  hard-remove its `PageMetadata` row (destroying history), and the `FileSystemWatcher`-driven
  reindex treated every rename as an unrelated delete+create pair (minting a new Id and
  orphaning history). Deletes are now soft (`PageMetadata.IsDeleted`/`DeletedAtUtc`/
  `DeletedByAppUserId`, row never removed) and both the in-app rename endpoint and the watcher's
  own rename handling update `RelativePath` on the existing row in place.
* [x] **Version coalescing** (`Services/VersionService.cs`) — a document's version is compared
  against its last *closed* (settled) state, not the immediately-preceding save: while content
  keeps differing from that baseline, repeated saves update one *open* version row in place
  instead of minting a new row per autosave; a save that lands back on the closed baseline
  discards the open version entirely (no history from a revert-within-session); an open version
  becomes permanently closed after 10 minutes of inactivity. The very first save for a document
  always produces a version, even with empty content.
* [x] **Restore never overwrites history** — `VersionService.CreateRestoreVersionAsync` always
  mints a new, immediately-closed version carrying the restored content (`VersionType.Restore`),
  closing any in-progress open version first. Works for both an old version of a live document
  (requires Edit permission, same as a normal save) and a soft-deleted document (requires
  Manage, same as deleting it did — restoring un-deletes it in the same step). Guards against a
  path collision if a different active document now occupies the deleted one's old path.
* [x] **GitHub-style diff viewer** (`frontend/src/components/diff.ts` + `DiffViewer.tsx`) — a
  hand-rolled LCS line diff (no new dependency; documents here are small Markdown pages, not
  source trees) rendered side-by-side with line numbers, added/removed highlighting, and long
  unchanged runs collapsed with click-to-expand context. Reused by both the per-document History
  panel and the admin Activity Log's "View Before/After".
* [x] **Per-document History panel** (toolbar "History" button → `VersionHistoryPanel.tsx`) —
  versions grouped by day, "Compare with Previous" (diffs a version against its predecessor),
  multi-select Compare, and Restore. Soft-deleted documents get a dedicated recovery banner in `PageView.tsx` (visible
  only to users who can already see them via the Manage-permission-filtered deleted-documents
  list — never leaks more than existing permissions already allow).
* [x] **Activity Log** (`Controllers/ActivityController.cs`, admin-only via the existing
  `RequireAdministrator` policy) — extends the pre-existing `AuditLogEntry`/`AuditLogService`
  audit trail in place (`ObjectType`/`ObjectId`/`IpAddress`/`RelatedVersionId`/
  `OccurrenceCount`/`LastOccurredAtUtc`) rather than standing up a parallel table. Covers auth
  (login/logout/rejected bearer tokens), file/folder create/modify/delete/restore/rename/move,
  and settings/permission changes. Server-side date-range/user/action/object filtering and
  pagination; defaults to the last 14 days, reachable back to the 30-day retention ceiling.
  `AuditLogService.LogGroupedAsync` coalesces repeated same-IP/same-action rows (e.g. a client
  repeatedly sending a rejected token) within a short window into one row with an occurrence
  count, instead of one row per occurrence — events are logged as exactly what they are; this
  app never sees actual password attempts at all, since Keycloak's own login page handles those
  entirely outside the API's view, so "failed login" isn't a real signal available to log.
* [x] **Configurable retention** (`Services/HistorySettingsService.cs`, `AppSetting`-backed like
  the AI model override) — `VersionHistoryRetentionDays` (default 3), `ActivityLogRetentionDays`
  (default 30), `ActivityLogDefaultDays` (default 14), editable from Admin → "Version history &
  activity log". `HistoryCleanupHostedService` runs once at startup and daily at 04:00 UTC,
  deleting only expired `DocumentVersion`/`AuditLogEntry` rows — never the current document
  state.
