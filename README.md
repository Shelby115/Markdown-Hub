# Markdown Hub

*Self-Hosted. Self-Referenced. Self-Owned.*

## Setup Guide

- Create your docker compose.
- Configure your environment.
- Optionally setup OIDC provider(s).
- Optionally setup your Ollama instance.

### Prerequisites

- Docker Compose
- Optional: Ollama for the AI Assistant functionality.

### 1. Configure `.env`

[Example Environment](./.env.example)

| Variable | Required | Notes |
|---|---|---|
| `ADMIN_USERNAME` | No (default `admin`) | The initial administrator's username. |
| `ADMIN_PASSWORD` | Recommended | The initial administrator's password, set on first boot only. Leave blank and you'll have no way to log in until you set one (see below). |
| `ADMIN_PASSWORD_FILE` | No | Path to a Docker/Podman secret file - takes precedence over `ADMIN_PASSWORD` if the file exists. |
| `HUB_HOST_PATH` | No | Where Markdown files live on the host. Relative paths are created automatically; point it at an existing folder to import an existing hub. Default `./data/markdown`. |
| `API_PORT` / `FRONTEND_PORT` | No | Default `8085` / `8086`. |
| `FRONTEND_ORIGIN` | Only if exposed beyond localhost | Your real external origin (scheme + host, no trailing slash) — added to allowed CORS origins and used as the default sign-in redirect target. |
| `OLLAMA_BASE_URL` / `OLLAMA_MODEL` | No | Optional AI-assisted editing, needs a reachable Ollama instance. App runs fine without it. |

`OIDC_DEFAULT_*`, `AUTH_PUBLIC_API_ORIGIN`, `JWT_SIGNING_KEY`, and `SESSION_LIFETIME_HOURS` are all optional too - see "External providers" and "Advanced" below.

### 2. Start it

[Example Compose](./docker-compose.yml)

- Frontend: `http://localhost:${FRONTEND_PORT:-8086}`
- API health check: `http://localhost:${API_PORT:-8085}/health` — checks app, hub directory, and
  database. An external OIDC provider being unconfigured or unreachable is reported but never
  makes the app "unhealthy" - local login never depends on one.

### 3. Log in

Sign in with `ADMIN_USERNAME` / `ADMIN_PASSWORD` from step 1.

### External providers (optional)

Add any number of external providers from **Admin → Authentication providers** at any time, no
restart or `.env` changes needed. Presets exist for Google, GitHub, Facebook, and Keycloak, plus
Generic OIDC / Generic OAuth 2.0 for anything else. For each you'll supply:

| Field | Notes |
|---|---|
| Client ID / Client secret | From your provider's app registration. The secret is encrypted at rest and never shown again after entry. |
| Authority (OIDC) | Issuer URL - discovery doc must be reachable at `<authority>/.well-known/openid-configuration`. |
| Authorization / Token / Userinfo endpoints (OAuth 2.0) | Pre-filled by the preset for known providers; enter manually for a custom OAuth 2.0 provider. |

**The redirect URI your provider needs is always
`<api-origin>/api/auth/external/<provider-name>/callback`** (e.g.
`http://localhost:8085/api/auth/external/keycloak/callback`) - the **API**, not the frontend,
since the server performs the authorization-code exchange itself. Your provider's client must
therefore be **confidential** (client secret required, not a public SPA client).

**Providers can never lock anyone out** - local username/password sign-in is always available,
and the app refuses any change (disabling/deleting a provider, removing your last linked
identity) that would leave the *last remaining administrator* with no way to sign in.

**Inviting someone else:** create their account from Admin → Users with a temporary password and
give it to them out of band. They log in locally once, then link Google/GitHub/etc. themselves
from their own Account page - accounts are never auto-linked by matching username or email.

### Advanced

- **`AUTH_PUBLIC_API_ORIGIN`** — only needed if the API sits behind a reverse proxy that doesn't
  forward the original scheme/host, leaving the app unable to compute a correct callback
  redirect URI for external providers.
- **`JWT_SIGNING_KEY`** — the app generates and persists its own on first boot; override only for
  a multi-instance deployment where every instance must share one key.
- **`SESSION_LIFETIME_HOURS`** (default 168, i.e. 7 days) — how long a login session lasts.
  Sessions can also be reviewed/revoked individually from Account → Sessions (or by an admin,
  for any user).
- **Hostnames the container can't reach**: a URL that works fine in your
  browser can still fail from inside the API container - an identity provider on your own LAN
  behind a router without NAT hairpin, or `host.docker.internal` on plain Docker Engine. Map it
  in a `docker-compose.override.yml` next to `docker-compose.yml` (Compose merges it
  automatically; keep it out of version control, it's host-specific):

  ```yaml
  services:
    api:
      extra_hosts:
        - "your-idp-hostname.example.com:host-gateway"
  ```

### AI assistant (optional)

Powers the AI Assistant side panel, backed by a local [Ollama](https://ollama.com) instance you
provide — nothing is sent to a third-party API. Everything else works fully without it.

1. Install Ollama and pull at least one model, e.g. `ollama pull gpt-oss:20b`.
2. Set two vars in `.env`:

   | Variable | Default | Notes |
   |---|---|---|
   | `OLLAMA_BASE_URL` | `http://host.docker.internal:11434` | Assumes Ollama runs on the same machine as Docker, outside any container. Point it elsewhere (another host, another container's service name) if that's not the case. |
   | `OLLAMA_MODEL` | `gpt-oss:20b` | Must match a model you've actually pulled. |

3. Restart the stack (`docker compose up -d`). Both are read on every container start, so
   changing them later just needs a restart.

If Ollama is unreachable or has no model installed, the panel says so upfront ("Ollama
installation not found...") instead of failing on first use — the fastest way to confirm it's
working. On plain Docker Engine (typical Linux), `host.docker.internal` doesn't resolve unless
you add it as an `extra_hosts` entry - see [Advanced](#advanced) above.

An admin can override the model at runtime from Admin → AI model without touching `.env` or
restarting — that override always takes precedence over `OLLAMA_MODEL`.


### Full Disclosure

I used AI to create this website. I know many hate AI and wanted to be transparent about it. AI was a means to an end. 
I'm a professional software developer and I've never had the motivation to finish work and turn around to do more programming.