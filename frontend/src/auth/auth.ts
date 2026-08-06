// Local username/password is the primary sign-in path; external providers (if any are enabled)
// are an alternative that redirects through the API, which performs the OIDC/OAuth2 exchange
// server-side and hands back a token this app minted itself (see backend AuthController). The
// token is a plain bearer JWT kept in localStorage and sent via the Authorization header - no
// silent-refresh dance is needed since sessions are simply long-lived (see Sessions:LifetimeHours);
// once a token expires the user just signs in again.

const TOKEN_KEY = "authToken";

export interface AuthProviderInfo {
  id: number;
  name: string;
  displayName: string;
  /// 0 = OIDC, 1 = OAuth 2.0 - mirrors the backend AuthProviderType enum (serialized numerically).
  type: number;
}

let providersPromise: Promise<AuthProviderInfo[]> | null = null;

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

function decodeExpiryMs(token: string): number | null {
  try {
    const payloadSegment = token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/");
    const payload = JSON.parse(atob(payloadSegment)) as { exp?: number };
    return typeof payload.exp === "number" ? payload.exp * 1000 : null;
  } catch {
    return null;
  }
}

export function getToken(): string | null {
  const token = localStorage.getItem(TOKEN_KEY);
  if (!token) return null;
  const expiresAtMs = decodeExpiryMs(token);
  if (expiresAtMs !== null && Date.now() >= expiresAtMs) {
    localStorage.removeItem(TOKEN_KEY);
    return null;
  }
  return token;
}

export function setToken(token: string): void {
  localStorage.setItem(TOKEN_KEY, token);
}

export function clearToken(): void {
  localStorage.removeItem(TOKEN_KEY);
}

export function isAuthenticated(): boolean {
  return getToken() !== null;
}

export async function loginLocal(username: string, password: string): Promise<void> {
  const res = await fetch("/api/auth/login", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ username, password }),
  });
  if (!res.ok) {
    const body = await res.json().catch(() => null as { message?: string } | null);
    throw new Error(body?.message ?? "Invalid username or password.");
  }
  const data = (await res.json()) as { token: string };
  setToken(data.token);
}

/** Full-page redirect through the API, which drives the server-side OIDC/OAuth2 exchange and
 * redirects back to /auth/callback with a token once it's done. */
export function startExternalLogin(providerName: string): void {
  const url = new URL(`/api/auth/external/${encodeURIComponent(providerName)}`, window.location.origin);
  url.searchParams.set("returnOrigin", window.location.origin);
  window.location.href = url.toString();
}

export function logout(): void {
  clearToken();
}
