# Authentication and Authorization Design

## 1. Purpose

The application must provide secure, self-contained authentication without requiring an external identity provider for initial setup.

Authentication providers such as Keycloak, Google, GitHub, and Facebook are optional integrations. They must enhance the application's authentication capabilities rather than becoming dependencies required to access the application.

The application must always provide a reliable administrative recovery path.

### Primary goals

1. The application must work immediately after installation without configuring an external identity provider.
2. A local administrator account must be created during initial setup.
3. Local username/password authentication must remain available unless the administrator explicitly disables it.
4. Users must be able to associate multiple authentication methods with one application account.
5. OIDC and OAuth 2.0 providers must be supported without making either protocol the foundation of the application's user model.
6. Removing or breaking an external provider must not inherently lock an administrator out of the application.
7. Authorization must be owned by the application, not delegated to an external identity provider.
8. Passwords and authentication secrets must follow modern security practices.
9. Authentication configuration must be manageable through the application UI.
10. The design must work cleanly in Docker/self-hosted deployments.

---

# 2. Authentication Architecture

Authentication and authorization are separate concerns.

## Authentication

Authentication answers:

> "Who is this person?"

The application may authenticate a user through:

* Local username/password
* OpenID Connect (OIDC)
* OAuth 2.0 identity providers
* Future authentication mechanisms

## Authorization

Authorization answers:

> "What is this user allowed to do?"

Authorization is always determined by the application's own user/account/role system.

An external provider must never directly determine whether a user is an administrator.

For example:

```text
Google
   |
   | authenticates user
   v
External Identity
   |
   v
Application User Account
   |
   +-- Roles
   +-- Permissions
   +-- Content ownership
   +-- Application settings
```

---

# 3. Application User Model

The application must have its own persistent user account.

A user account should contain information conceptually equivalent to:

```text
User
----
Id
Username
NormalizedUsername
Email
NormalizedEmail
DisplayName
PasswordHash
IsActive
CreatedAt
UpdatedAt
LastLoginAt
```

`PasswordHash` may be null for an account that has no local password, although the initial administrator account must have one.

The application must not use an external provider's user ID as the application's primary user ID.

---

# 4. Authentication Identity Model

A user may have multiple authentication identities.

Conceptually:

```text
User
 |
 +-- AuthenticationIdentity
 |      Provider = Local
 |      Subject = local:<user-id>
 |
 +-- AuthenticationIdentity
        Provider = Keycloak
        Subject = <OIDC subject>
```

Another user might have:

```text
User
 |
 +-- Local password
 +-- Google
 +-- GitHub
```

An authentication identity should contain information conceptually equivalent to:

```text
AuthenticationIdentity
----------------------
Id
UserId
ProviderId
ProviderSubject
CreatedAt
LastUsedAt
```

The combination of:

```text
ProviderId + ProviderSubject
```

must be unique.

The external provider's `sub`/subject identifier must be treated as the authoritative immutable identifier for that external identity.

Email addresses must **not** be used as the sole identity key.

---

# 5. Local Authentication

Local authentication is the foundational authentication mechanism.

It must be available immediately after installation.

## Login

The application must provide a normal login form:

```text
Username
Password
[Sign In]
```

External providers may appear as additional buttons:

```text
Sign in with Google
Sign in with GitHub
Sign in with Keycloak
```

The absence or failure of an external provider must not prevent the local login form from functioning.

---

# 6. Password Handling

Passwords must never be stored in plaintext.

Passwords must be stored using a password-specific, deliberately slow, salted password hashing algorithm.

The implementation should use the password hashing facilities provided by the application's authentication framework rather than implementing cryptographic password hashing manually.

Preferred algorithms, in order of availability:

1. Argon2id
2. scrypt
3. PBKDF2 with a strong work factor

The implementation must support automatic rehashing when the configured password hashing parameters become stronger.

The application must never:

* Log passwords.
* Store plaintext passwords.
* Store reversible encrypted passwords.
* Return password hashes to the frontend.
* Include passwords in API responses.
* Include passwords in exception messages.
* Include passwords in analytics or telemetry.
* Email passwords to users.

## Password requirements

Do not impose arbitrary complexity rules such as:

> Must contain an uppercase letter, lowercase letter, number, symbol, etc.

Prefer a reasonable minimum length and allow long passphrases.

Passwords should be accepted up to a sufficiently large maximum length to allow password managers and passphrases.

Passwords must be compared through the password-hashing framework rather than manually comparing hashes.

---

# 7. Password Change

Authenticated users must be able to change their password.

Changing a password should require:

1. Current password.
2. New password.
3. Confirmation of new password.

After changing the password, the application should invalidate other active authentication sessions for that user where practical.

The current session may remain active or be explicitly reauthenticated according to the application's security policy.

Administrators changing another user's password should not need to know the user's existing password.

---

# 8. Initial Administrator Account

The application must create an initial administrator account during first-time initialization.

The administrator credentials must be configurable through deployment configuration.

Recommended Docker Compose configuration:

```yaml
environment:
  ADMIN_USERNAME: admin
  ADMIN_PASSWORD: ${ADMIN_PASSWORD}
```

The password should normally be placed in `.env` rather than directly in `docker-compose.yml`.

The application should additionally support a secret-file configuration:

```yaml
environment:
  ADMIN_USERNAME: admin
  ADMIN_PASSWORD_FILE: /run/secrets/admin_password
```

## Bootstrap behavior

The environment variables are bootstrap configuration, not a permanent authentication mechanism.

On first initialization:

```text
ADMIN_USERNAME
       |
ADMIN_PASSWORD
       |
       v
Create administrator account
       |
       v
Hash password
       |
       v
Store password hash in database
```

After the account exists, changing the password in the UI changes the database credential.

The application must not continuously compare the database password against `ADMIN_PASSWORD`.

Changing `ADMIN_PASSWORD` in Docker Compose must not unexpectedly overwrite an existing administrator password.

---

# 9. First-Run Behavior

The first-run experience should be:

```text
docker compose up -d
        |
        v
Application initializes
        |
        v
Initial administrator account created
        |
        v
User opens application
        |
        v
Local login
        |
        v
Application is ready
```

No external authentication provider should be required.

If an administrator has configured Google, GitHub, Keycloak, Facebook, or another provider, those providers may appear on the login page.

---

# 10. Administrator Recovery

The system must maintain a reliable authentication recovery path.

The application must prevent an administrator from accidentally removing their final authentication method.

For example, if an administrator has:

```text
Local password
Keycloak
```

they may remove Keycloak because local authentication remains available.

If the administrator only has:

```text
Keycloak
```

the application must not allow them to remove Keycloak unless another authentication method has first been established.

The final administrator account must always retain at least one usable authentication method.

The application must also prevent deletion/deactivation of the final active administrator when doing so would leave the application without an administrator.

---

# 11. External Identity Providers

External providers are optional.

The application should support two provider categories:

```text
OIDC Provider
OAuth 2.0 Provider
```

They should share a common application-level identity abstraction.

## OIDC

OIDC should be used for providers that support OpenID Connect.

Configuration should conceptually include:

```text
Name
Display Name
Issuer / Authority
Client ID
Client Secret
Scopes
Enabled
Auto-link policy
```

OIDC discovery should be preferred over manually specifying every endpoint.

The application should validate:

* Issuer
* Audience
* Signature
* Expiration
* Nonce where applicable
* State
* PKCE where applicable
* Redirect URI
* Required claims

The provider's `sub` claim is the external identity key.

---

# 12. OAuth 2.0 Providers

The architecture must also support providers that authenticate users through OAuth 2.0 without issuing standard OIDC ID tokens.

GitHub is an example of this category for user authentication.

A provider adapter should be able to:

1. Redirect the user to the provider.
2. Receive the authorization callback.
3. Exchange the authorization code for an access token.
4. Retrieve the user's identity through the provider's supported API.
5. Extract a stable provider-specific subject/user ID.
6. Associate that identity with an application user.

The OAuth implementation must use authorization-code flow.

Do not implement the OAuth implicit flow.

Provider-specific behavior should be isolated behind an abstraction rather than scattered throughout the application.

---

# 13. Built-In Provider Presets

The application should ship with provider presets for common services.

Initial presets:

* Google
* GitHub
* Facebook
* Keycloak

These presets should make configuration easier but must not contain application-specific secrets.

For example:

```text
Google
  Protocol: OIDC
  Issuer: Google
  Client ID: [administrator supplies]
  Client Secret: [administrator supplies]

GitHub
  Protocol: OAuth 2.0
  Client ID: [administrator supplies]
  Client Secret: [administrator supplies]

Facebook
  Protocol: OAuth 2.0
  Client ID: [administrator supplies]
  Client Secret: [administrator supplies]

Keycloak
  Protocol: OIDC
  Issuer: [administrator supplies]
  Client ID: [administrator supplies]
  Client Secret: [administrator supplies]
```

"Available out of the box" means the provider configuration exists in the application and can be selected without manually implementing the provider.

It does **not** mean the application contains shared client credentials.

Provider secrets must always be supplied by the deployment owner.

---

# 14. External Account Linking

External authentication must support linking to an existing application account.

Example:

```text
Existing account
    |
    v
Account Settings
    |
    v
Authentication
    |
    v
Add Google
    |
    v
Authenticate with Google
    |
    v
Google identity linked to existing account
```

After linking:

```text
Application User
 |
 +-- Local password
 |
 +-- Google identity
 |
 +-- Keycloak identity
```

All authentication methods authenticate the same application user.

---

# 15. Automatic Account Creation

The application may optionally create a new application account when a user successfully authenticates through an external provider.

This behavior must be configurable per provider.

Recommended options:

```text
Allow automatic account creation
Require administrator approval
Disable automatic account creation
```

For a self-hosted private installation, automatic creation may be enabled by default for configured external providers.

For an administrator-controlled installation, automatic account creation should be configurable.

---

# 16. Account Linking Security

The application must never automatically merge two existing accounts solely because their email addresses match.

Example:

```text
Local User A
email = user@example.com

Google User B
email = user@example.com
```

These must remain separate unless the application can establish that the user intentionally wants to link them.

Email may be used as a helpful lookup during an explicit linking flow, but it must not be treated as proof of account ownership.

A user must authenticate to the existing account before linking another authentication identity.

---

# 17. Provider Removal

Administrators must be able to remove external providers.

Before removal, the application should display:

```text
This provider is currently used by 3 users.

Removing it will prevent those users from signing in
through this provider.

Continue?
```

The application must prevent removal when doing so would remove the final authentication method for the last administrator.

Provider configuration deletion must not delete the application users associated with that provider.

It only removes the ability to authenticate through that provider.

---

# 18. Provider Availability

A provider being configured does not necessarily mean it is enabled.

Providers should have an explicit:

```text
Enabled / Disabled
```

state.

The login page should only display enabled providers.

A provider configuration failure must not disable local authentication.

If an external provider becomes unavailable, users must still be able to authenticate through their other linked methods.

---

# 19. Sessions and Cookies

The application should use secure server-managed authentication cookies rather than storing authentication tokens in browser local storage.

Authentication cookies should use:

```text
HttpOnly
Secure
SameSite
```

with appropriate values for the application's deployment model.

Session lifetime should be configurable.

The application should support session revocation.

Administrators should be able to invalidate a user's active sessions.

Users should be able to see and revoke their own active sessions if practical.

---

# 20. CSRF Protection

All state-changing authenticated browser requests must have appropriate CSRF protection.

The OIDC/OAuth callback flow must also validate the appropriate `state` value.

Where supported, PKCE should be used for authorization-code flows.

---

# 21. Rate Limiting and Brute-Force Protection

Local authentication must have protection against password guessing.

At minimum:

* Rate-limit login attempts.
* Apply progressive delays or temporary lockouts after repeated failures.
* Avoid revealing whether a username exists.
* Log authentication failures without logging passwords.
* Avoid permanent account lockouts that could be used for denial-of-service attacks.

The exact thresholds should be configurable.

---

# 22. Authorization

Authorization belongs entirely to the application.

Initial roles:

```text
Administrator
User
```

The authorization system should be extensible so additional roles/permissions can be introduced later.

## Administrator

Administrators may:

* Manage users.
* Manage authentication providers.
* Manage application configuration.
* Manage authorization.
* Access administrative functionality.

## User

Normal users may:

* Access functionality granted to normal users.
* Manage their own profile.
* Manage their own authentication methods.
* Manage their own sessions.

Authorization checks must be performed server-side.

The frontend must never be treated as the authority for access control.

---

# 23. External Provider Claims

External provider claims may be used to populate profile information such as:

```text
Email
Display name
First name
Last name
Profile image
```

External claims must not automatically grant administrative privileges.

Do not implement behavior such as:

```text
email ends with @company.com => administrator
```

unless such functionality is explicitly designed, documented, and secured as an authorization feature.

Provider claims are authentication/profile information, not application authorization.

---

# 24. Secrets

Provider client secrets must be treated as secrets.

They must not be:

* Committed to source control.
* Written to logs.
* Returned through APIs.
* Included in frontend JavaScript.
* Displayed in plaintext after initial entry.

The UI should display configured secrets in masked form.

The database should protect provider secrets appropriately for the application's threat model.

Docker secrets or environment-based secret injection should be supported where practical.

---

# 25. Database Model

The implementation should introduce the equivalent of the following conceptual entities:

```text
Users
-----
Id
Username
NormalizedUsername
Email
NormalizedEmail
DisplayName
PasswordHash
IsActive
CreatedAt
UpdatedAt
LastLoginAt


AuthenticationProviders
-----------------------
Id
Name
DisplayName
Type
Configuration
Enabled
CreatedAt
UpdatedAt


AuthenticationIdentities
------------------------
Id
UserId
AuthenticationProviderId
Subject
CreatedAt
LastUsedAt
```

Additional entities may be introduced for:

```text
Sessions
Roles
Permissions
UserRoles
ProviderSecrets
AuditEvents
```

The actual schema should follow the project's existing persistence architecture.

Do not duplicate user identity information unnecessarily.

---

# 26. Auditing

Authentication-related security events should be auditable.

Examples:

```text
Login succeeded
Login failed
Password changed
Password reset
Authentication identity linked
Authentication identity removed
Provider created
Provider modified
Provider enabled
Provider disabled
Provider deleted
User created
User disabled
Administrator privileges changed
Session revoked
```

Audit records must not contain passwords, client secrets, access tokens, refresh tokens, or other sensitive credentials.

---

# 27. Administrative UI

The administrator should have an authentication management area approximately like:

```text
Administration
└── Authentication
    ├── Users
    ├── Providers
    └── Security
```

Provider management should support:

```text
+ Add Provider

Provider
  Name
  Type
  Enabled
  Client ID
  Client Secret
  Configuration
  Users Using Provider
```

The UI should provide provider presets:

```text
Add Authentication Provider

[ Google ]
[ GitHub ]
[ Facebook ]
[ Keycloak ]
[ Generic OIDC ]
[ Generic OAuth 2.0 ]
```

---

# 28. User Authentication UI

The user account page should provide:

```text
Account
├── Profile
├── Password
├── Authentication Methods
└── Sessions
```

Authentication Methods should show:

```text
Local Password       Connected
Google               Connected
GitHub               Not connected
Keycloak              Connected
```

Users can add/remove authentication methods subject to the same safety rules that protect the final administrator authentication method.

---

# 29. Migration From the Existing Authentication System

The current authentication architecture is based around pre-configured external OIDC providers.

This architecture must be replaced rather than preserved as the required authentication path.

Migration requirements:

1. Existing users must not be unnecessarily destroyed.
2. Existing application user IDs should remain stable where possible.
3. Existing external identity relationships should be migrated into `AuthenticationIdentities`.
4. Existing provider configuration should be migrated into `AuthenticationProviders`.
5. Local authentication must be introduced.
6. An administrator must always have a recovery path.
7. Existing Keycloak configuration should remain usable after migration.
8. The migration must not require Keycloak to be available during application startup.

If an automatic migration cannot safely preserve an existing identity relationship, the migration should fail clearly rather than silently creating duplicate accounts.

---

# 30. Non-Goals

This design does not require:

* Keycloak.
* Any external identity provider.
* A separate identity-management server.
* External authentication for initial setup.
* Provider-specific application authorization.
* Password storage outside the application database.
* A permanent dependency on any particular identity provider.

---

# 31. Security Invariants

The following must always be true:

1. Passwords are never stored in plaintext.
2. Passwords are never logged.
3. External provider credentials are never exposed to the browser.
4. External provider identities never directly determine application authorization.
5. A provider's email address is not sufficient by itself to merge two existing accounts.
6. An administrator cannot accidentally remove their final authentication method.
7. The application can authenticate locally without an external provider.
8. Removing an external provider does not delete application accounts.
9. Disabling an external provider does not disable local authentication.
10. The application never depends on an external provider being available during startup.
11. Authorization is enforced server-side.
12. Authentication failures cannot permanently lock the application out of its administrators through ordinary provider-management operations.

---

# 32. Desired User Experience

The finished application should make this possible:

```text
docker compose up -d
        |
        v
Open application
        |
        v
Log in with administrator username/password
        |
        v
Application works
```

From there:

```text
Administration
    |
    +-- Add Google
    +-- Add GitHub
    +-- Add Facebook
    +-- Add Keycloak
    +-- Add Generic OIDC
    +-- Add Generic OAuth 2.0
```

External authentication should be an enhancement to the application, not a prerequisite for using it.

The fundamental principle is:

> **The application owns the user account. Authentication providers are merely ways to prove ownership of that account.**
