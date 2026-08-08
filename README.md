# Markdown Hub

*Self-Hosted. Self-Referenced. Self-Owned.*

A self-hosted, web-based Markdown hub. Your `.md` files stay on your own server's
filesystem as plain files — that's the source of truth, not a database — while a
browser-based editor gives you live preview, wiki-style links, full-text search,
and backlinks on top of them.

## What problem it solves

Most note apps that offer sync, sharing, and web publishing want a monthly
subscription, and they keep your notes in their storage in their format. Markdown
Hub is the self-hosted alternative: point it at a folder of Markdown files, run it
on your own machine, and get the wiki-style editing and publishing experience
without handing your notes to anyone.

Because the files on disk stay ordinary Markdown, nothing here locks you in. Git
pull into that folder, edit with another editor, or walk away entirely — the app
notices external changes and reindexes them, and your notes are just files.

## Features

**Editing**
- Live-preview editor (CodeMirror 6). Markdown renders styled by default; raw
  syntax reveals only for the element your cursor is actually on.
- Wiki-style `[[links]]` and `[[Page|aliases]]`, resolved hub-wide by filename.
  Links to pages that don't exist yet render distinctly.
- Embeds: `![[image.png]]`, `![[song.mp3]]`, `![[clip.mp4]]`, `![[Handbook.pdf]]`,
  and `![[note transclusion]]` for inlining another page's rendered content.
- Auto-save with a 2-second debounce, plus conflict detection — a concurrent edit
  writes a `.conflict.<timestamp>.md` copy instead of silently overwriting.
- Undo/redo, image paste-to-upload, and page templates.
- An inline dice roller: `2d20+1`, `+d20` (advantage), `-d20` (disadvantage) render
  as clickable buttons. Your Markdown source is never rewritten.

**Organizing and finding**
- Folder tree with inline create, rename, move, and delete for both files and folders.
- Full-text search across page names, folders, and content (SQLite FTS5), with
  snippet highlighting, filtered to what you're allowed to see.
- Backlinks panel, kept in sync on every save.
- A per-user home folder the tree auto-expands to.

**Sharing and history**
- Publishing: toggle a page public and get an unguessable slug at
  `/published/:slug` — read-only, no login, and it never exposes filesystem paths
  or the existence of unpublished pages.
- Version history with a GitHub-style side-by-side diff, compare, and restore.
  Deleted pages are soft-deleted and recoverable.
- Admin activity log covering sign-ins, file and folder changes, and settings
  changes, with filtering and pagination.

**Administration**
- Folder-level permissions (View / Edit / Manage) that inherit by path prefix.
- Local username/password accounts, plus optional linked external sign-in providers.
- Zip backups (Markdown, database, config, attachments) — manual or daily, with
  retention.
- Optional AI assistant panel backed by your own local [Ollama](https://ollama.com)
  instance. Nothing is ever sent to a third-party API.
- AI Templates: turn a template into a generator whose sections you can reroll,
  improve, and lock individually. See below.
- Generation pools: pre-generate template content in the background on a schedule you
  choose, so filling a placeholder is instant instead of a wait.

## Prerequisites

- **Docker** with **Docker Compose**. That's the whole requirement.
- Optional: an [Ollama](https://ollama.com) instance for the AI assistant. Everything
  else works fully without it.

For working on the code instead of just running it, see [CONTRIBUTING.md](CONTRIBUTING.md).

## Quick start

```bash
git clone <your-fork-or-this-repo-url> markdown-hub
cd markdown-hub
cp .env.example .env
```

Edit `.env` and set at minimum:

- `ADMIN_PASSWORD` — the initial administrator password.
- `HUB_HOST_PATH` — the folder on the host holding your Markdown files. Leave the
  default `./data/markdown` to start fresh, or point it at an existing folder to
  adopt notes you already have.

Then start it:

```bash
docker compose up -d --build
```

This builds both images from source; the first build takes a few minutes.

### Or run the prebuilt images

If you'd rather not build anything, `docker-compose.ghcr.yml` pulls published images from
GitHub Container Registry. You only need that file and a `.env` — no source checkout:

```bash
docker compose -f docker-compose.ghcr.yml up -d
```

Same two settings (`HUB_HOST_PATH`, `ADMIN_PASSWORD`); everything else has a working default.
This setup doesn't publish the API port at all — the frontend proxies to it over the Compose
network, so only `FRONTEND_PORT` is reachable from the host.

## Accessing it

| | URL |
|---|---|
| Web app | `http://localhost:8086` |
| API | `http://localhost:8085` (source build only) |
| Health check | `http://localhost:8086/health` |

Sign in with the `ADMIN_USERNAME` (default `admin`) and `ADMIN_PASSWORD` you set.

The health check reports on the app, the hub directory, and the database. An external
identity provider being unconfigured or unreachable is reported for information but
never marks the app unhealthy — local sign-in never depends on one.

You don't have to expose the API port publicly. The frontend container proxies `/api`
and `/health` through to the API over the internal Docker network, so the web app works
with only `FRONTEND_PORT` reachable.

## Configuration

All configuration is environment variables, read from `.env` by Docker Compose. Nothing
listed here is required except a password to log in with.

| Variable | Default | Notes |
|---|---|---|
| `ADMIN_USERNAME` | `admin` | Initial administrator's username. |
| `ADMIN_PASSWORD` | *(empty)* | Initial administrator's password. Set this, or you'll have no way to sign in. Applied **only** on first boot against an empty database. |
| `ADMIN_PASSWORD_FILE` | *(empty)* | Path to a Docker/Podman secret file, e.g. `/run/secrets/admin_password`. Takes precedence over `ADMIN_PASSWORD` when the file exists. |
| `HUB_HOST_PATH` | `./data/markdown` | Host folder holding your Markdown files. Relative paths are created automatically; absolute paths (e.g. `F:/Notes`, `/srv/notes`) let you adopt an existing hub. |
| `API_PORT` | `8085` | Host port for the API. |
| `FRONTEND_PORT` | `8086` | Host port for the web app. |
| `FRONTEND_ORIGIN` | *(empty)* | Your real external origin (scheme + host, no trailing slash) if you expose this beyond localhost. Added to allowed CORS origins and used as the default sign-in redirect target. |
| `ASPNETCORE_ENVIRONMENT` | `Production` | Leave as `Production`. `Development` enables Swagger and detailed errors, which leak stack traces and paths. |
| `SESSION_LIFETIME_HOURS` | `168` | How long a session lasts before re-authentication. Default is 7 days. |
| `OLLAMA_BASE_URL` | `http://host.docker.internal:11434` | Only for the AI assistant. |
| `OLLAMA_MODEL` | `gpt-oss:20b` | Must match a model you've actually pulled. |
| `OIDC_DEFAULT_*` | *(empty)* | Optional shortcut to seed one external provider on first boot — see [External sign-in providers](#external-sign-in-providers-optional). |
| `AUTH_PUBLIC_API_ORIGIN` | *(empty)* | Only if a reverse proxy doesn't forward the original scheme/host — see [Reverse proxy and HTTPS](#reverse-proxy-and-https). |
| `JWT_SIGNING_KEY` | *(auto)* | The app generates and persists its own on first boot. Override only when running multiple instances that must share one key. |

Changing `ADMIN_USERNAME` or `ADMIN_PASSWORD` after the account exists does nothing —
the database is authoritative from then on. Change the password from the Account or
Admin page instead.

## Storage and volumes

Your Markdown is a bind mount to a real host folder. Everything else lives in named
Docker volumes.

| Path in container | Backed by | Holds |
|---|---|---|
| `/data/markdown` | bind mount from `HUB_HOST_PATH` | **Your Markdown files and attachments.** Plain files — back these up like any other folder. |
| `/data/db` | `db-data` volume | SQLite database: accounts, permissions, search index, version history, activity log. Never file content. |
| `/data/keys` | `keys-data` volume | Data Protection key ring used to encrypt external-provider client secrets at rest. |
| `/data/backups` | `backup-data` volume | Generated zip backups. |

Two things worth knowing:

- **`keys-data` must survive redeploys.** If you lose it, every stored external-provider
  client secret becomes undecryptable and has to be re-entered from the admin page.
- **Losing `db-data` doesn't lose your notes**, but it does lose accounts, permissions,
  version history, and the activity log. The search index and file metadata rebuild
  themselves from disk on startup.

Built-in backups (Admin page, or daily at 03:00 UTC, keeping the last 14) bundle the
Markdown, the database, config, and attachments into a zip in `/data/backups`. Because
that's a Docker volume, copy backups off the host if you want them somewhere safer.

## Authentication

**Local username/password is the foundation and always works.** No external identity
provider is required, ever.

The first boot against an empty database creates one administrator from
`ADMIN_USERNAME`/`ADMIN_PASSWORD`. After that the database is authoritative. Passwords
are hashed with ASP.NET Core Identity's PBKDF2-HMAC-SHA256 and rehashed on verify.

Sign-in issues a JWT that the app signs itself. Every token carries a session ID tied to
a database row, so sessions stay individually revocable — review or revoke them from
Account → Sessions, or as an admin, for any user. Failed sign-ins are rate-limited and
logged by username and IP, never with the password.

**Adding people:** create the account from Admin → Users with a temporary password and
give it to them out of band. Accounts are never auto-created or auto-linked by matching
username or email.

### External sign-in providers (optional)

Any number of OIDC or OAuth 2.0 providers can be added from **Admin → Authentication
providers** at any time — no restart, no `.env` changes. Presets exist for Google,
GitHub, Facebook, and Keycloak, plus Generic OIDC and Generic OAuth 2.0. Users link
them to their own account from their Account page.

The API performs the authorization-code exchange server-side (PKCE and state validated),
so provider tokens and client secrets never reach the browser. Two consequences:

- **Your provider's client must be confidential** — a client secret is required. A public
  SPA client won't work.
- **The redirect URI is always `<api-origin>/api/auth/external/<provider-name>/callback`**
  — pointing at the API, not the frontend. For a default local setup that's
  `http://localhost:8085/api/auth/external/keycloak/callback`.

`keycloak/markdown-hub-client-import.json` is an example confidential-client config you
can import into a Keycloak realm. Adjust the redirect URI to match your deployment.

Admin versus regular user is always tracked in this app's own database — never delegated
to a provider's roles or claims. And providers can never lock anyone out: local sign-in
always remains available, and the app refuses any change that would leave the last
administrator with no way to sign in.

`OIDC_DEFAULT_NAME`, `_AUTHORITY`, `_CLIENT_ID`, `_CLIENT_SECRET`, `_AUDIENCE`, and
`_REQUIRE_HTTPS_METADATA` seed a single provider on a brand-new database as a shortcut
past the admin page. They need `_AUTHORITY`, `_CLIENT_ID`, and `_CLIENT_SECRET` set
together, apply only while no provider exists yet, and are ignored afterwards.

## Reverse proxy and HTTPS

**Put this behind HTTPS if it's reachable beyond localhost.** Sign-in credentials and a
bearer token cross the wire on every request. Audio, video, and PDF embeds additionally
pass the token as an `access_token` query parameter, because `<audio>`, `<video>`, and
`<iframe>` cannot send an `Authorization` header — scoped to `/api/attachments` only, but
it does mean tokens can land in access logs over plain HTTP.

The simplest setup is to point one reverse proxy at `FRONTEND_PORT` and leave the API
port unpublished. The frontend's nginx already forwards `/api` and `/health` to the API
container, so a single origin serves everything — and the external-provider callback URL
becomes `https://notes.example.com/api/auth/external/<name>/callback`.

Your proxy must set `X-Forwarded-Proto`. The API trusts `X-Forwarded-Proto`, `-Host`, and
`-For`, and the bundled nginx passes through whatever the outer proxy set. Without it the
app believes requests arrived over plain HTTP and builds `http://` callback redirect URIs,
which your identity provider will reject.

A minimal nginx example:

```nginx
server {
    listen 443 ssl;
    server_name notes.example.com;

    # ssl_certificate / ssl_certificate_key ...

    location / {
        proxy_pass http://127.0.0.1:8086;
        proxy_set_header Host              $host;
        proxy_set_header X-Forwarded-Proto $scheme;
        proxy_set_header X-Forwarded-Host  $host;
        proxy_set_header X-Forwarded-For   $proxy_add_x_forwarded_for;
    }
}
```

Then set `FRONTEND_ORIGIN=https://notes.example.com` in `.env` so that origin is allowed
for CORS and used as the sign-in redirect target.

If your proxy can't forward the original scheme and host, set `AUTH_PUBLIC_API_ORIGIN` to
the public origin of the API instead, and callback URIs will be built from that rather
than from the incoming request.

### Hostnames the container can't reach

A URL that works in your browser can still fail from inside the API container — an
identity provider on your own LAN behind a router without NAT hairpin, or
`host.docker.internal` on plain Docker Engine. Map it in a `docker-compose.override.yml`
next to `docker-compose.yml`. Compose merges it automatically and it's gitignored,
so it stays local to that machine:

```yaml
services:
  api:
    extra_hosts:
      - "your-idp-hostname.example.com:host-gateway"
```

## AI assistant (optional)

Powers the AI Assistant side panel using an Ollama instance you run yourself.

1. Install Ollama and pull a model: `ollama pull gpt-oss:20b`
2. Set `OLLAMA_BASE_URL` and `OLLAMA_MODEL` in `.env`. The default base URL assumes
   Ollama runs on the Docker host itself, outside any container.
3. Restart: `docker compose up -d`

If Ollama is unreachable or has no model, the panel says so upfront rather than failing
on first use — the quickest way to confirm it's wired up. On plain Docker Engine
(typical on Linux) `host.docker.internal` doesn't resolve unless you add it via
`extra_hosts`, as above.

An admin can override the model at runtime from the **AI** page (sidebar → AI) without touching `.env`
or restarting. That override takes precedence over `OLLAMA_MODEL`.

## AI Templates (optional)

An AI Template is an ordinary template page that generates its own content. Write the
document's structure with `{{Placeholder}}` markers, then describe what each placeholder
should produce in a fenced ` ```ai-template ` block:

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

Tick **Template** in the editor toolbar, then start a generation either way:

- From the template page itself — a **✨ Generate** button appears in the toolbar next to the
  Template checkbox. You choose where the finished page is saved.
- From the file tree — `⋮` → **New page from template**, exactly like an ordinary template.
  The generation panel opens instead of the fill-in-the-blank prompt.

The panel generates every section immediately when it opens, one at a time, each one aware of
what came before. From there:

- **Reroll** (🎲) regenerates a single section; **Improve** (✨) revises it while keeping its
  subject; **Lock** (🔒) freezes it, and locked sections become context for everything
  generated afterward. Each section is also editable by hand.
- **Regenerate all** re-runs every unlocked section.
- A repeated placeholder becomes that many independent sections — four `{{Interactible}}`
  lines always produce exactly four interactibles, since the count comes from your template
  rather than from the model.
- **Save as page** writes ordinary Markdown. Nothing about the result is special afterward:
  it's versioned, indexed, and editable like any other page.

Recognized instruction rules: `Format:`, `Example:`, `Max words: N`, and
`Max sentences: N` are checked after generation (a section that fails is regenerated once,
then shown with a warning rather than being thrown away). Every other `- bullet` is free
text passed to the model as-is. Examples are explicitly treated as formatting samples the
model is told never to reuse.

Placeholders with no entry in the instruction block stay ordinary fill-in-the-blank
variables, so a template can mix both. A template with no ` ```ai-template ` block behaves
exactly as it always has.

Generation is one model call per placeholder, so a large template takes a while on a local
model — `Ai:Ollama:TimeoutSeconds` applies to each section, not the whole document. Progress
appears section by section and can be stopped partway; whatever generated already is kept.
Closing the panel discards the session, so save before you walk away.

### Generation pools (pre-generating in the background)

The wait above is the point of generation pools. A pool is a named library of pre-written
content for one kind of placeholder — interactibles, NPC names, rumours. A background service
fills it while nothing else is happening, and a template that uses it gets an entry straight
out of the database instead of waiting on the model.

Create one on the **AI** page (sidebar → AI) under **Generation pools**: give it a name, a prompt (the same bullet
rules a template's instruction block uses, `Format:`/`Example:`/`Max words:`/`Max sentences:`
included), and how many entries to keep ready. Tick "Generate entries for this pool in the
background" when you're happy with the prompt — nothing runs against Ollama until you do.
**Generate one now** produces a single entry immediately, which is the quickest way to see
whether a prompt edit did what you wanted.

Point a template at the pool by adding one line to that placeholder:

````markdown
```ai-template
Interactible:
- Pool: Interactible
```
````

The pool's prompt then replaces the template's own rules for that placeholder. Pool entries
are written without knowing anything about the rest of the page, so pools suit self-contained
items rather than sections that have to match their surroundings — keep those as ordinary
placeholders. If the pool runs dry, or the name doesn't match any pool, generation falls back
to a live model call, so a template never breaks.

Every entry is handed out at most once. If one is bad, **🚫** on that section forgets it: it's
never shown again, and never regenerated either. Admins can do the same from the pool's entry
list.

The background generator has app-wide controls in the same admin section:

- **Pause / Resume**, taking effect on the next tick without a restart.
- An **allowed window** (`22:00`–`06:00` UTC, say) so generation only happens overnight. Times
  are UTC and the current server time is shown next to them; leave both blank to allow any
  hour. A window whose end is earlier than its start wraps past midnight.
- **Seconds between entries** — the generator adds at most one entry per pool per tick, so a
  longer interval leaves more of the machine free.
- **Keep used entries (days)** — how long spent entries are retained. They exist only so the
  same text isn't generated twice; the daily 04:00 UTC cleanup removes expired ones. Forgotten
  entries are never removed, which is what makes forgetting permanent.

## Updating

Both images build from source, so updating means pulling the code and rebuilding:

```bash
git pull
docker compose up -d --build
```

Database schema changes are applied automatically on startup. Your Markdown is a bind
mount and the database, keys, and backups are named volumes, so none of it is touched by
a rebuild — `docker compose up --build` does not remove volumes.

Take a backup before updating anyway. Note that `docker compose down -v` **does** delete
the named volumes — that's your database and encryption keys, not your notes.

## Notes and limitations

- **Uploading through the app is images-only.** Paste-to-upload accepts PNG, JPEG, GIF,
  WebP, and SVG, validated by extension *and* magic bytes, up to 20 MB. Audio, video, and
  PDF files can be embedded and played, but you place those in your hub folder yourself.
- **Version history defaults to 3 days** of retention, the activity log to 30. Both are
  configurable from Admin → Version history & activity log. Cleanup runs at 04:00 UTC and
  only ever removes expired history rows, never current documents.
- **Backups run daily at 03:00 UTC.** The `BackupSchedule` setting is not yet honored for
  arbitrary cron expressions.
- Embeds inside table cells render as plain text. Wiki-links in table cells do work.
- Dice notation is interactive in the editor only. In published pages and transcluded
  content it renders as plain text.

## Security

For reporting vulnerabilities and for deployment expectations, see
[SECURITY.md](SECURITY.md).

Briefly: every path derived from user input is resolved against the hub root and rejected
if it escapes; all rendered Markdown is sanitized after conversion through a single choke
point; uploads are checked by extension and magic bytes; and production mode returns
generic errors so stack traces and filesystem paths never reach the browser.

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md). Open an issue before starting anything
substantial.

## License

Licensed under the **GNU Affero General Public License v3.0**. See [LICENSE](LICENSE).

The AGPL's network clause matters for a project like this: if you run a modified version
as a service other people can use over a network, you have to offer them the source of
your modifications.

## Full Disclosure

I used AI to create this website. I know many hate AI and wanted to be transparent about it. AI was a means to an end.
I'm a professional software developer and I've never had the motivation to finish work and turn around to do more programming.
