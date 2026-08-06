# Markdown Hub

*Self-Hosted. Self-Referenced. Self-Owned.*

## Setup Guide

The app is fully usable with **local username/password login only** — no external identity
provider is required, ever. External providers (Google, GitHub, Facebook, Keycloak, or any
other OIDC/OAuth 2.0 provider) are an optional enhancement any user can link to their account
once the app is up.

### Prerequisites

- Docker + Docker Compose.
- That's it. An external identity provider is entirely optional - see "External providers" below.

### 1. Configure `.env`

```bash
cp .env.example .env
```

| Variable | Required | Notes |
|---|---|---|
| `ADMIN_USERNAME` | No (default `admin`) | The initial administrator's username. |
| `ADMIN_PASSWORD` | Recommended | The initial administrator's password, set on first boot only. Leave blank and you'll have no way to log in until you set one (see below). |
| `ADMIN_PASSWORD_FILE` | No | Path to a Docker/Podman secret file - takes precedence over `ADMIN_PASSWORD` if the file exists. |
| `HUB_HOST_PATH` | No | Where Markdown files live on the host. Relative paths are created automatically; point it at an existing folder to import an existing hub. Default `./data/markdown`. |
| `API_PORT` / `FRONTEND_PORT` | No | Default `8085` / `8086`. |
| `FRONTEND_ORIGIN` | Only if exposed beyond localhost | Your real external origin (scheme + host, no trailing slash) — added to allowed CORS origins and used as the default sign-in redirect target. |
| `OLLAMA_BASE_URL` / `OLLAMA_MODEL` | No | Optional AI-assisted editing, needs a reachable Ollama instance. App runs fine without it. |

`OIDC_DEFAULT_*`, `AUTH_PUBLIC_API_ORIGIN`, `JWT_SIGNING_KEY`, and `SESSION_LIFETIME_HOURS` are
all optional too - see "External providers" and "Advanced" below.

### 2. Start it

```bash
docker compose up --build
```

- Frontend: `http://localhost:${FRONTEND_PORT:-8086}`
- API health check: `http://localhost:${API_PORT:-8085}/health` — checks app, hub directory, and
  database. An external OIDC provider being unconfigured or unreachable is reported but never
  makes the app "unhealthy" - local login never depends on one.

### 3. Log in

Sign in with `ADMIN_USERNAME` / `ADMIN_PASSWORD` from step 1. That's it - the app is fully
usable from here with zero external providers configured.

**Didn't set `ADMIN_PASSWORD` before first boot?** The admin account is never created without a
password (so you can't end up with a reserved-but-unusable username). Set `ADMIN_PASSWORD` in
`.env` now and run `docker compose up -d` again - the account will be created on this boot
instead. If a database already exists from a previous run *without* an admin account, this still
works the same way, since seeding only ever creates a brand-new row, never overwrites one.

### External providers (optional)

Add any number of external providers from **Admin → Authentication providers** at any time, no
restart or `.env` changes needed. Presets exist for Google, GitHub, Facebook, and Keycloak, plus
Generic OIDC / Generic OAuth 2.0 for anything else. For each provider you'll supply:

| Field | Notes |
|---|---|
| Client ID / Client secret | From your provider's app registration. The secret is encrypted at rest and never shown again after entry. |
| Authority (OIDC) | Issuer URL - discovery doc must be reachable at `<authority>/.well-known/openid-configuration`. |
| Authorization / Token / Userinfo endpoints (OAuth 2.0) | Pre-filled by the preset for known providers; enter manually for a custom OAuth 2.0 provider. |

**The redirect URI your provider needs is always
`<api-origin>/api/auth/external/<provider-name>/callback`** (e.g.
`http://localhost:8085/api/auth/external/keycloak/callback` for local Docker Compose use) - the
**API**, not the frontend, since the server performs the authorization-code exchange itself.
This means your provider's client must be **confidential** (client secret required, not a public
SPA client) — a change from earlier versions of this app.

**Removing every provider, or all of them failing, never locks anyone out** — local
username/password sign-in is always available as a fallback, and the app refuses any action
(disabling/deleting a provider, removing your last linked identity) that would leave the *last
remaining administrator* with no way to sign in at all.

**Inviting someone else:** create their account from Admin → Users with a temporary password,
and give it to them out of band. They log in locally once, then link Google/GitHub/etc.
themselves from their own Account page - accounts are never auto-linked by matching username or
email, only by a user explicitly linking a provider to an account they're already signed into.

#### Seeding one provider on first boot (optional convenience)

If you'd rather not click through the admin page on a brand-new install, `OIDC_DEFAULT_*` env
vars seed one OIDC provider automatically the first time the app boots against an empty
database - purely a convenience; skip it and add providers via the admin page instead. Requires
`OIDC_DEFAULT_AUTHORITY`, `OIDC_DEFAULT_CLIENT_ID`, and now also `OIDC_DEFAULT_CLIENT_SECRET`
(see `.env.example`) all set together, or nothing is seeded. Only read once, on an empty
database - editing `.env` afterward has no effect.

### Advanced

- **`AUTH_PUBLIC_API_ORIGIN`** — only needed if the API sits behind a reverse proxy that doesn't
  forward the original scheme/host, so the app can't otherwise compute a correct callback
  redirect URI for external providers.
- **`JWT_SIGNING_KEY`** — the app generates and persists its own signing key on first boot; only
  override this for a multi-instance deployment where every instance must share one key.
- **`SESSION_LIFETIME_HOURS`** (default 168, i.e. 7 days) — how long a login session lasts before
  needing to sign in again. Sessions can also be reviewed/revoked individually from Account →
  Sessions (or by an admin, for any user).
- **NAT hairpin / loopback**: if an external provider you're using runs on the same host/LAN but
  your router can't route a public hostname back to itself (common on home networks), requests
  from inside the API container to the provider's public URL will fail even though the same URL
  works fine from a browser. Fix: add a `docker-compose.override.yml` next to
  `docker-compose.yml` (Compose merges it automatically; keep it out of version control, it's
  host-specific):

  ```yaml
  services:
    api:
      extra_hosts:
        - "your-idp-hostname.example.com:host-gateway"
  ```

### Upgrading from an older version (provider-only auth)

Older versions of this app required an external OIDC provider for every login, with no local
password option. On first boot after upgrading:

- Existing users keep their accounts, permissions, and history - nothing is deleted.
- Your existing OIDC provider is migrated into the new admin page, but comes across **disabled**
  and without a client secret (the old flow never needed one). To keep using it: reconfigure its
  client as **confidential** (add a secret), update its redirect URI to the new
  `<api-origin>/api/auth/external/<name>/callback` shape, enter the secret and re-enable the
  provider from Admin → Authentication providers.
- Existing users' linked identity is preserved automatically *if* you only ever had one OIDC
  provider configured. If you had more than one, the app can't safely guess which provider each
  user authenticated through (the old design never recorded that) - those users keep their
  accounts but will need an admin to set a temporary password for them (Admin → Users → Set
  password) so they can log in locally once and re-link their provider themselves.
- Set `ADMIN_PASSWORD` before this upgrade's first boot so you have a guaranteed way in
  regardless of provider state; otherwise you'll need to either fix up the migrated provider
  first, or set the password afterward and restart (see "Log in" above).

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

3. Restart the stack (`docker compose up -d`). These aren't a one-time seed — they're read on
   every container start, so changing them later just needs a restart, not a database reset.

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
