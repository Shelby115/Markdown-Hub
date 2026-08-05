# Markdown Hub

*Self-Hosted. Self-Referenced. Self-Owned.*

## Setup Guide

Self-hosting checklist. There's no bundled identity provider and no local/password login —
you bring your own OIDC provider, and the very first thing to get right is making sure at
least one of them works, since there is no other way into the app (not even the admin page).

### Prerequisites

- Docker + Docker Compose.
- An OIDC provider you control (Keycloak, Authentik, Zitadel, Auth0, ...) that can act as a **public SPA client**: authorization code + PKCE, no client secret.

### 1. Set up your identity provider

Whatever provider you use, its client needs:

| Requirement | Value |
|---|---|
| Client type | Public (no client secret / "client authentication" off) |
| Flow | Authorization code + PKCE (S256) |
| Redirect URI | `<your frontend origin>/auth/callback` (e.g. `http://localhost:8086/auth/callback`) |
| Audience claim | The token's `aud` must contain a value you'll put in `OIDC_DEFAULT_AUDIENCE` |

**Keycloak specifics:** import `keycloak/markdown-hub-client-import.json` into your realm as a
starting point. It's a public client with PKCE required and an audience mapper already
attached — **Keycloak does not include a client's own ID in `aud` by default**, so without
that mapper (or an equivalent one you add yourself) every token gets rejected as a bad
audience. If you use a different provider, check whether it needs the same kind of explicit
audience configuration.

### 2. Configure `.env`

```bash
cp .env.example .env
```

| Variable | Required | Notes |
|---|---|---|
| `OIDC_DEFAULT_AUTHORITY` | Yes (first boot) | Issuer URL. Discovery doc must be reachable at `<authority>/.well-known/openid-configuration`. |
| `OIDC_DEFAULT_CLIENT_ID` | Yes (first boot) | The public client ID from step 1. |
| `OIDC_DEFAULT_AUDIENCE` | Yes (first boot) | Must match the `aud` your provider's tokens actually carry. |
| `OIDC_DEFAULT_NAME` | No | Display label (login screen / admin page). Default `Default`. |
| `OIDC_DEFAULT_REQUIRE_HTTPS_METADATA` | No | Set `false` only if the authority is `http://` (e.g. a same-network dev IdP). Default `true`. |
| `ADMIN_USERNAME` | No, but recommended | See below. |
| `HUB_HOST_PATH` | No | Where Markdown files live on the host. Relative paths are created automatically; point it at an existing folder to import an existing hub. Default `./data/markdown`. |
| `API_PORT` / `FRONTEND_PORT` | No | Default `8085` / `8086`. |
| `FRONTEND_ORIGIN` | Only if exposed beyond localhost | Your real external origin (scheme + host, no trailing slash) — added to allowed CORS origins. Forgetting this means every API call from your real domain gets CORS-blocked even though login itself works. |
| `OLLAMA_BASE_URL` / `OLLAMA_MODEL` | No | Optional AI-assisted editing, needs a reachable Ollama instance. App runs fine without it. |

**`OIDC_DEFAULT_*` only matters once** — the app reads it exactly once, to seed the very first
row of the `OidcProviders` table on a brand-new database. After that boot, these env vars are
dead: providers are managed entirely through Admin → OIDC providers, and editing `.env` later
does nothing. This is also why getting step 1 right *before* first boot matters so much — get
it wrong and you have no provider to log in with, and thus no way to reach the admin page to
fix it. If that happens, the fastest way out is stopping the stack, deleting the SQLite DB
volume, correcting `.env`, and starting fresh (fine before real data exists; not something to
do once you have real content).

### 3. (Optional) NAT hairpin / loopback

If your identity provider and this app run on the same host/LAN but your router can't route a
public hostname back to itself (common on home networks), requests from inside the API
container to your provider's public URL will fail even though the same URL works fine from a
browser. Fix: add a `docker-compose.override.yml` next to `docker-compose.yml` (Compose merges
it automatically; keep it out of version control, it's host-specific):

```yaml
services:
  api:
    extra_hosts:
      - "your-idp-hostname.example.com:host-gateway"
```

### 4. Start it

```bash
docker compose up --build
```

- Frontend: `http://localhost:${FRONTEND_PORT:-8086}`
- API health check: `http://localhost:${API_PORT:-8085}/health` — checks app, hub directory,
  database, and OIDC provider reachability. Start here if something's wrong; it'll say which
  piece is broken.

### 5. Becoming admin

Two ways, don't need both:

- **`ADMIN_USERNAME`** — guarantees that exact username becomes admin the moment it first signs
  in. It's create-only: if that username already exists as a non-admin, this does *not*
  retroactively promote them (use Admin → Users → Promote for that instead).
- **First login ever** — if `ADMIN_USERNAME` isn't set, whoever signs in first automatically
  becomes admin. Fine for a single-operator instance; race-prone if multiple people might sign
  in before you do.

### Managing providers after setup

Admin → OIDC providers: add/edit/enable/disable/delete providers at runtime, no restart or env
changes needed. **At least one enabled provider must always exist** — the app refuses to
delete or disable the last one, so you can't lock yourself out *that* way once you're past first
boot. With more than one enabled provider, the login screen shows a picker instead of
auto-redirecting.

**That safety net does not cover *editing* a provider.** Updating an existing (possibly your
only) provider's Client ID/Audience/Authority to something that doesn't match what your IdP
actually issues locks out everyone immediately, including admins - there's no equivalent "don't
save if this breaks the last working provider" check on Update, and if it's your only provider
you can no longer reach Admin to undo it through the UI.

**Safer pattern:** when changing an *already-working* provider's settings (e.g. switching to a
new IdP client), add the new configuration as a **second, separate provider** first and leave
the old one enabled. Test signing in with the new one from the login screen's picker. Only once
that works, disable/delete the old provider. Editing a working provider in place should be a
last resort, not the default move.

**If you do lock yourself out** (every provider now issues tokens the app rejects, so nobody -
not even an admin - can sign in to fix it from Admin): the fix has to happen directly against
the database, since there's no other way in.

1. Find the API's SQLite database. In the default Docker Compose setup it's inside the
   `db-data` named volume (`<project-name>_db-data`; the exact name depends on your Compose
   project/folder name - `docker volume ls` will show it) as `/data/db/markdown-hub.db`.
2. Inspect the `OidcProviders` table and fix whichever column is wrong (usually `Audience` or
   `ClientId`) to match what your identity provider's client is actually configured to issue,
   e.g. via a throwaway container:
   ```bash
   docker run --rm -v <project-name>_db-data:/db alpine sh -c \
     "apk add --no-cache sqlite && sqlite3 /db/markdown-hub.db \
      \"SELECT * FROM OidcProviders;\""
   ```
   then, once you know what's wrong:
   ```bash
   docker run --rm -v <project-name>_db-data:/db alpine sh -c \
     "apk add --no-cache sqlite && sqlite3 /db/markdown-hub.db \
      \"UPDATE OidcProviders SET Audience = 'the-correct-value' WHERE Id = <id>;\""
   ```
3. **Restart the `api` container** (`docker compose restart api`). The corrected row alone
   isn't enough - provider config is cached in memory for up to 60 seconds per request and
   indefinitely if nothing triggers a refresh, so a stale rejection can otherwise persist well
   past when the database itself is already fixed, making the fix look like it didn't work.
4. Try signing in again from a fresh page load.

### Other gotchas

- **Redirect URI mismatch** is the most common first-login failure — it must be exactly
  `<origin>/auth/callback`, including scheme and port.
- **Audience mismatch** is the second most common — `OIDC_DEFAULT_AUDIENCE` must equal a value
  actually present in your tokens' `aud`, which for many providers (Keycloak included) isn't
  there by default and needs explicit client configuration.
- The app never stores or needs a client secret — if your provider insists on one, you've
  configured it as a confidential client instead of a public one.
- `RequireHttpsMetadata=true` (the default) will refuse an `http://` authority outright; only
  turn it off for a same-network/dev IdP you trust.
- Nothing here bypasses OIDC — there is no local admin login, "setup mode," or password
  fallback. Get the identity provider working first; everything else follows from that.

### AI assistant (optional)

Entirely optional — everything else works fully without it. Enables the AI-assisted editing
toolbar and the AI Assistant side panel, both backed by a local [Ollama](https://ollama.com)
instance you provide; nothing is sent to a third-party API.

1. Install Ollama and pull at least one model, e.g. `ollama pull gpt-oss:20b`.
2. Set two vars in `.env`:

   | Variable | Default | Notes |
   |---|---|---|
   | `OLLAMA_BASE_URL` | `http://host.docker.internal:11434` | Assumes Ollama runs on the same machine as Docker, outside any container. Point it elsewhere (another host, another container's service name) if that's not the case. |
   | `OLLAMA_MODEL` | `gpt-oss:20b` | Must match a model you've actually pulled. |

3. Restart the stack (`docker compose up -d`). Unlike the OIDC variables, these aren't a
   one-time seed — they're read on every container start, so changing them later just needs a
   restart, not a database reset.

If Ollama is unreachable or has no model installed, the AI Assistant panel says so directly
("Ollama installation not found...") instead of failing silently on first use — that's the
fastest way to confirm it's working.

**Linux gotcha:** `host.docker.internal` resolves automatically on Docker Desktop (Mac/Windows)
but not on plain Docker Engine (the typical Linux setup) unless added explicitly. If Ollama
runs on the same Linux host as Docker, add to `docker-compose.override.yml`:

```yaml
services:
  api:
    extra_hosts:
      - "host.docker.internal:host-gateway"
```

An admin can override the model at runtime from Admin → AI model without touching `.env` or
restarting — that override always takes precedence over `OLLAMA_MODEL`.
