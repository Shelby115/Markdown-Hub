import { User, UserManager, WebStorageStateStore } from "oidc-client-ts";

export interface AuthProviderInfo {
  id: number;
  name: string;
  authority: string;
  clientId: string;
}

// Which provider to resume a session against on reload, and which one a redirect callback
// should complete against - sessionStorage (not localStorage) since it only needs to survive
// the single redirect round-trip / the current tab's session.
const SELECTED_PROVIDER_KEY = "oidc.selectedProviderId";

let providersPromise: Promise<AuthProviderInfo[]> | null = null;
let userManager: UserManager | null = null;
let currentUser: User | null = null;

async function fetchProviders(): Promise<AuthProviderInfo[]> {
  const res = await fetch("/api/auth/providers");
  if (!res.ok) throw new Error(`Couldn't load sign-in providers (${res.status}).`);
  return res.json();
}

/** Cached for the page's lifetime - the provider list only changes via the admin page, which
 * requires a fresh sign-in to reach anyway. */
export function getProviders(): Promise<AuthProviderInfo[]> {
  providersPromise ??= fetchProviders();
  return providersPromise;
}

function buildUserManager(provider: AuthProviderInfo): UserManager {
  return new UserManager({
    authority: provider.authority,
    client_id: provider.clientId,
    redirect_uri: window.location.origin + "/auth/callback",
    post_logout_redirect_uri: window.location.origin,
    response_type: "code",
    scope: "openid profile email",
    userStore: new WebStorageStateStore({ store: window.localStorage }),
  });
}

async function resolveSelectedProvider(explicitProviderId?: number): Promise<AuthProviderInfo> {
  const providers = await getProviders();
  if (providers.length === 0) throw new Error("No sign-in provider is configured.");

  const storedId = sessionStorage.getItem(SELECTED_PROVIDER_KEY);
  const wantedId = explicitProviderId ?? (storedId ? Number(storedId) : undefined);
  const provider = wantedId !== undefined
    ? providers.find((p) => p.id === wantedId)
    : providers.length === 1
      ? providers[0]
      : undefined;

  if (!provider) throw new Error("A sign-in provider must be chosen.");
  sessionStorage.setItem(SELECTED_PROVIDER_KEY, String(provider.id));
  return provider;
}

async function getUserManager(explicitProviderId?: number): Promise<UserManager> {
  if (userManager && explicitProviderId === undefined) return userManager;
  const provider = await resolveSelectedProvider(explicitProviderId);
  userManager = buildUserManager(provider);
  return userManager;
}

/**
 * Checks for an existing, still-usable session without redirecting anywhere - used on page
 * load. Returns the provider list alongside auth state so App.tsx can decide whether to show
 * the single "Sign in" screen (0-1 providers, or a provider already selected) or a picker
 * (multiple providers, none selected yet).
 */
export async function initAuth(): Promise<{ authenticated: boolean; providers: AuthProviderInfo[] }> {
  const providers = await getProviders();
  if (providers.length === 0) return { authenticated: false, providers };

  const storedId = sessionStorage.getItem(SELECTED_PROVIDER_KEY);
  const resolvable = providers.length === 1 || (storedId !== null && providers.some((p) => String(p.id) === storedId));
  if (!resolvable) return { authenticated: false, providers };

  const manager = await getUserManager();
  currentUser = await manager.getUser();
  if (currentUser && !currentUser.expired) return { authenticated: true, providers };

  if (currentUser?.refresh_token) {
    try {
      currentUser = await manager.signinSilent();
      return { authenticated: currentUser !== null, providers };
    } catch {
      // Refresh failed (e.g. session revoked at the provider) - fall through to signed-out.
    }
  }
  return { authenticated: false, providers };
}

/** Starts the redirect-based sign-in flow. providerId is required once more than one provider
 * is enabled; with exactly one, it's inferred. */
export async function login(providerId?: number): Promise<void> {
  const manager = await getUserManager(providerId);
  await manager.signinRedirect();
}

/** Completes the redirect back from the provider - call from the /auth/callback route. */
export async function completeLogin(): Promise<void> {
  const manager = await getUserManager();
  currentUser = await manager.signinRedirectCallback();
}

export async function logout(): Promise<void> {
  const manager = await getUserManager();
  currentUser = null;
  await manager.signoutRedirect();
}

/** Returns a still-valid access token, refreshing it first if needed, or forces a re-login if
 * the session can't be refreshed. */
export async function getFreshToken(): Promise<string | undefined> {
  const manager = await getUserManager();
  currentUser ??= await manager.getUser();

  if (currentUser && !currentUser.expired) return currentUser.access_token;

  if (currentUser?.refresh_token) {
    try {
      currentUser = await manager.signinSilent();
      if (currentUser) return currentUser.access_token;
    } catch {
      // fall through to re-login
    }
  }
  await login();
  return undefined;
}

export function getUsername(): string | undefined {
  const profile = currentUser?.profile;
  return (profile?.preferred_username as string | undefined) ?? profile?.email ?? profile?.sub;
}
