import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { clearToken, getToken, isAuthenticated, loginLocal, setToken } from "./auth";

function makeToken(claims: Record<string, unknown>): string {
  const base64url = (obj: unknown) =>
    btoa(JSON.stringify(obj)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
  return `${base64url({ alg: "HS256" })}.${base64url(claims)}.signature`;
}

describe("token storage", () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => {
    localStorage.clear();
    vi.unstubAllGlobals();
  });

  it("returns null when no token is stored", () => {
    expect(getToken()).toBeNull();
    expect(isAuthenticated()).toBe(false);
  });

  it("returns a stored, unexpired token", () => {
    const token = makeToken({ exp: Math.floor(Date.now() / 1000) + 3600 });
    setToken(token);

    expect(getToken()).toBe(token);
    expect(isAuthenticated()).toBe(true);
  });

  it("clears and returns null for an expired token", () => {
    const token = makeToken({ exp: Math.floor(Date.now() / 1000) - 3600 });
    setToken(token);

    expect(getToken()).toBeNull();
    expect(localStorage.getItem("authToken")).toBeNull();
  });

  it("clearToken removes the stored token", () => {
    setToken(makeToken({ exp: Math.floor(Date.now() / 1000) + 3600 }));

    clearToken();

    expect(getToken()).toBeNull();
  });
});

describe("loginLocal", () => {
  beforeEach(() => localStorage.clear());
  afterEach(() => vi.unstubAllGlobals());

  it("stores the returned token on success", async () => {
    const token = makeToken({ exp: Math.floor(Date.now() / 1000) + 3600 });
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: true,
        json: async () => ({ token, expiresAt: new Date().toISOString() }),
      })
    );

    await loginLocal("alice", "correct horse battery staple");

    expect(getToken()).toBe(token);
  });

  it("throws the server's error message on failure", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn().mockResolvedValue({
        ok: false,
        json: async () => ({ message: "Invalid username or password." }),
      })
    );

    await expect(loginLocal("alice", "wrong")).rejects.toThrow("Invalid username or password.");
    expect(getToken()).toBeNull();
  });
});
