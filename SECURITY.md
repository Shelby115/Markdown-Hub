# Security Policy

## Reporting a vulnerability

**Please do not open a public issue for security problems.**

Report privately through GitHub's private vulnerability reporting: go to the
repository's **Security** tab → **Report a vulnerability**. That opens a private
advisory visible only to you and the maintainer.

Useful things to include:

- What the issue is and roughly how severe you think it is.
- Steps to reproduce, or a proof of concept.
- The commit you tested against, and whether you ran it behind a reverse proxy.

This is a small self-hosted project maintained by one person in their spare time,
so please don't expect a same-day reply. You'll get an acknowledgement when the
report is read, and an update once it's been looked at. Please give a reasonable
window for a fix before disclosing publicly.

## Scope

Markdown Hub is self-hosted software — there is no service to attack, so reports
should be about the code in this repository. Things that are in scope:

- Path traversal or any way to read/write files outside the configured hub directory.
- Authentication or session-handling flaws (token forgery, session fixation,
  bypassing session revocation).
- Authorization flaws — reading or editing a folder you don't have permission for,
  or reaching admin-only endpoints as a regular user.
- XSS or HTML-sanitizer bypasses in rendered Markdown.
- Leaking unpublished content or filesystem paths through the unauthenticated
  `/published/:slug` route or the anonymous attachment endpoint.
- Attachment-upload validation bypasses.

Out of scope: findings that require an attacker to already have administrator
access, and misconfiguration of your own deployment (see below).

## Deployment expectations

A few things are the operator's responsibility rather than bugs in this project:

- **Run it behind HTTPS** if it's reachable beyond localhost. Bearer tokens are
  sent on every request, and audio/video/PDF embeds pass a token as a query
  parameter (see the README's reverse-proxy section).
- **Set a strong `ADMIN_PASSWORD`** on first boot, or use `ADMIN_PASSWORD_FILE`.
- **Keep the `keys-data` volume.** It holds the Data Protection key ring used to
  encrypt external-provider client secrets at rest. Lose it and those secrets
  become undecryptable and have to be re-entered.
- **Don't expose the API port publicly** unless you need to. The frontend
  container already proxies `/api` to it.

## Known design decisions

These are deliberate, documented trade-offs rather than oversights:

- `/published/:slug` is intentionally unauthenticated. It resolves wiki-links only
  to other published pages and never exposes filesystem paths.
- Attachment requests may carry an `access_token` query parameter, scoped to
  `/api/attachments` only, because `<audio>`/`<video>`/`<iframe>` cannot send an
  `Authorization` header. The endpoint still runs the normal permission check.
- Editing an external authentication provider is not validated against what the
  provider actually issues, so a bad edit can break that provider until it's
  corrected. It cannot lock anyone out, because local username/password sign-in
  is always available.
