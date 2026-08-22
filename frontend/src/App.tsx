import { FormEvent, useEffect, useRef, useState } from "react";
import { Route, Routes, useLocation, useMatch, useNavigate } from "react-router-dom";
import { api } from "./api/client";
import { AuthProviderInfo, getProviders, isAuthenticated, loginLocal, logout, startExternalLogin } from "./auth/auth";
import { Account } from "./pages/Account";
import { Admin } from "./pages/Admin";
import { ActivityLogPage } from "./pages/ActivityLogPage";
import { AuthCallback } from "./pages/AuthCallback";
import { AiAssistantPanel } from "./components/AiAssistantPanel";
import { FileTree } from "./components/FileTree";
import { SearchBar } from "./components/SearchBar";
import { PageView } from "./pages/PageView";
import { AiLab } from "./pages/AiLab";
import { PublishedPage } from "./pages/PublishedPage";
import { Welcome } from "./pages/Welcome";
import { toPageUrl } from "./pageUrl";

const SIDEBAR_COLLAPSED_KEY = "sidebarCollapsed";
const ASSISTANT_COLLAPSED_KEY = "assistantCollapsed";
const THEME_KEY = "themePreference";

type ThemePreference = "light" | "dark" | "system";

function resolveTheme(pref: ThemePreference): "light" | "dark" {
  if (pref === "system") {
    return window.matchMedia("(prefers-color-scheme: dark)").matches ? "dark" : "light";
  }
  return pref;
}

export default function App() {
  const location = useLocation();
  // Published pages are meant to be viewable by anyone with the link, no login - so this
  // route is handled entirely separately, before auth even enters the picture.
  const isPublishedRoute = location.pathname.startsWith("/published/");
  const isAuthCallbackRoute = location.pathname === "/auth/callback";

  const [ready, setReady] = useState(false);
  const [authenticated, setAuthenticated] = useState(false);
  const [authError, setAuthError] = useState<string | null>(null);
  const [providers, setProviders] = useState<AuthProviderInfo[]>([]);
  const [loginUsername, setLoginUsername] = useState("");
  const [loginPassword, setLoginPassword] = useState("");
  const [loginBusy, setLoginBusy] = useState(false);
  const [isAdmin, setIsAdmin] = useState(false);
  const [username, setUsername] = useState<string | undefined>(undefined);
  const [defaultFolderPath, setDefaultFolderPath] = useState<string | null>(null);
  const [sidebarCollapsed, setSidebarCollapsed] = useState(
    () => localStorage.getItem(SIDEBAR_COLLAPSED_KEY) === "true"
  );
  const [assistantCollapsed, setAssistantCollapsed] = useState(
    () => localStorage.getItem(ASSISTANT_COLLAPSED_KEY) !== "false"
  );
  const [themePreference, setThemePreference] = useState<ThemePreference>(
    () => (localStorage.getItem(THEME_KEY) as ThemePreference | null) ?? "system"
  );
  // Bumped after the assistant panel adds content to the currently open page, forcing PageView
  // to remount and refetch fresh content rather than showing stale in-memory state.
  const [reloadNonce, setReloadNonce] = useState(0);
  const [treeNonce, setTreeNonce] = useState(0);
  const navigate = useNavigate();
  const openPage = (relativePath: string) => navigate(toPageUrl(relativePath));

  useEffect(() => {
    localStorage.setItem(SIDEBAR_COLLAPSED_KEY, String(sidebarCollapsed));
  }, [sidebarCollapsed]);

  useEffect(() => {
    localStorage.setItem(ASSISTANT_COLLAPSED_KEY, String(assistantCollapsed));
  }, [assistantCollapsed]);

  const sidebarRef = useRef<HTMLElement>(null);

  const isMobileViewport = () => window.matchMedia("(max-width: 768px)").matches;

  // On a phone the sidebar and AI panel are full-width-ish overlays (see index.css), so having
  // both open at once just buries the content between two drawers - opening one closes the
  // other. Desktop keeps its existing side-by-side behavior untouched.
  const openSidebar = () => {
    setSidebarCollapsed(false);
    if (isMobileViewport()) setAssistantCollapsed(true);
  };
  const closeSidebar = () => setSidebarCollapsed(true);
  const openAssistant = () => {
    setAssistantCollapsed(false);
    if (isMobileViewport()) setSidebarCollapsed(true);
  };
  const closeAssistant = () => setAssistantCollapsed(true);

  // Swipe gestures for the mobile drawers. Each panel gets its own two zones, based on where the
  // panel actually sits on screen rather than swipe direction alone:
  //   - closed: a swipe starting in that panel's edge sliver pulls it out.
  //   - open: a swipe starting anywhere on the panel itself (it covers most of the screen) sends
  //     it back off.
  // Direction still has to match (rightward to reveal the left panel or dismiss the right one,
  // leftward for the mirror case), but gating on start position too is what keeps "swipe from the
  // right" from being swallowed by "the left panel happens to be open" and closing that instead.
  // Listens on the capture phase so it still sees the gesture even if CodeMirror or another child
  // stops propagation on the bubble phase.
  useEffect(() => {
    const EDGE_ZONE = 24;
    const SWIPE_THRESHOLD = 60;
    let startX = 0;
    let startY = 0;
    let tracking = false;

    const onTouchStart = (e: TouchEvent) => {
      if (e.touches.length !== 1) {
        tracking = false;
        return;
      }
      startX = e.touches[0].clientX;
      startY = e.touches[0].clientY;
      tracking = true;
    };

    const onTouchEnd = (e: TouchEvent) => {
      if (!tracking) return;
      tracking = false;
      if (!isMobileViewport()) return;
      const touch = e.changedTouches[0];
      const dx = touch.clientX - startX;
      const dy = touch.clientY - startY;
      if (Math.abs(dx) < SWIPE_THRESHOLD || Math.abs(dx) < Math.abs(dy)) return;

      const sidebarWidth = sidebarRef.current?.getBoundingClientRect().width ?? 0;
      const assistantWidth = document.querySelector(".ai-assistant-panel")?.getBoundingClientRect().width ?? 0;

      if (dx > 0) {
        // Rightward: dismiss the AI panel back to the right, or pull the sidebar in from the left.
        if (!assistantCollapsed && startX >= window.innerWidth - assistantWidth) closeAssistant();
        else if (sidebarCollapsed && startX < EDGE_ZONE) openSidebar();
      } else {
        // Leftward: dismiss the sidebar back to the left, or pull the AI panel in from the right.
        if (!sidebarCollapsed && startX <= sidebarWidth) closeSidebar();
        else if (assistantCollapsed && startX > window.innerWidth - EDGE_ZONE) openAssistant();
      }
    };

    document.addEventListener("touchstart", onTouchStart, { capture: true, passive: true });
    document.addEventListener("touchend", onTouchEnd, { capture: true, passive: true });
    return () => {
      document.removeEventListener("touchstart", onTouchStart, { capture: true });
      document.removeEventListener("touchend", onTouchEnd, { capture: true });
    };
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [sidebarCollapsed, assistantCollapsed]);

  // Applies regardless of auth/route (including the unauthenticated /published/:slug view), so
  // every visitor - not just signed-in users - gets a themed page defaulting to their system
  // preference. Only signed-in users see the toggle buttons that set an explicit override.
  useEffect(() => {
    localStorage.setItem(THEME_KEY, themePreference);
    const apply = () => document.documentElement.setAttribute("data-theme", resolveTheme(themePreference));
    apply();
    if (themePreference !== "system") return;
    const media = window.matchMedia("(prefers-color-scheme: dark)");
    media.addEventListener("change", apply);
    return () => media.removeEventListener("change", apply);
  }, [themePreference]);

  // Drives the file tree's "currently open" highlight/auto-expand - re-derive the raw
  // relativePath (with .md) of whatever page the route is currently showing, if any.
  const pageMatch = useMatch("/page/*");
  const activePath = pageMatch?.params["*"] ? `${pageMatch.params["*"]}.md` : null;

  // Tracks whatever page was open immediately before the current one, so deleting the page
  // you're looking at can return you there instead of always dropping back to the blank home
  // screen. Refs (not state) since this is read-only bookkeeping - it should never itself
  // trigger a re-render.
  const previousActivePathRef = useRef<string | null>(null);
  const lastSeenActivePathRef = useRef<string | null>(activePath);
  useEffect(() => {
    if (lastSeenActivePathRef.current !== activePath) {
      previousActivePathRef.current = lastSeenActivePathRef.current;
      lastSeenActivePathRef.current = activePath;
    }
  }, [activePath]);

  useEffect(() => {
    if (isPublishedRoute || isAuthCallbackRoute) return;
    setAuthenticated(isAuthenticated());
    setReady(true);
    getProviders()
      .then(setProviders)
      .catch((err) => {
        console.error("Couldn't load sign-in providers", err);
        setProviders([]);
      });
  }, [isPublishedRoute, isAuthCallbackRoute]);

  useEffect(() => {
    if (!authenticated) return;
    api
      .getMe()
      .then((me) => {
        setIsAdmin(me.isAdministrator);
        setUsername(me.username);
        setDefaultFolderPath(me.defaultFolderPath);
      })
      .catch(() => setIsAdmin(false));
  }, [authenticated]);

  const submitLogin = async (event: FormEvent) => {
    event.preventDefault();
    setLoginBusy(true);
    setAuthError(null);
    try {
      await loginLocal(loginUsername.trim(), loginPassword);
      setLoginPassword("");
      setAuthenticated(true);
    } catch (err) {
      setAuthError(err instanceof Error ? err.message : String(err));
    } finally {
      setLoginBusy(false);
    }
  };

  if (isPublishedRoute) {
    return (
      <Routes>
        <Route path="/published/:slug" element={<PublishedPage />} />
      </Routes>
    );
  }

  if (isAuthCallbackRoute) {
    return (
      <Routes>
        <Route path="/auth/callback" element={<AuthCallback />} />
      </Routes>
    );
  }

  if (!ready) {
    return <div className="splash">Loading hub…</div>;
  }

  if (!authenticated) {
    return (
      <div className="splash">
        <h1>Markdown Hub</h1>
        <p className="tagline">Self-Hosted. Self-Referenced. Self-Owned.</p>
        {authError && <p style={{ color: "crimson" }}>{authError}</p>}
        <form className="admin-grant-form" onSubmit={(e) => void submitLogin(e)}>
          <input
            type="text"
            placeholder="Username"
            autoComplete="username"
            value={loginUsername}
            onChange={(e) => setLoginUsername(e.target.value)}
          />
          <input
            type="password"
            placeholder="Password"
            autoComplete="current-password"
            value={loginPassword}
            onChange={(e) => setLoginPassword(e.target.value)}
          />
          <button className="primary" type="submit" disabled={loginBusy || !loginUsername.trim() || !loginPassword}>
            Sign In
          </button>
        </form>
        {providers.length > 0 && (
          <div className="admin-grant-form">
            {providers.map((p) => (
              <button key={p.id} className="secondary" onClick={() => startExternalLogin(p.name)}>
                Sign in with {p.displayName}
              </button>
            ))}
          </div>
        )}
      </div>
    );
  }

  return (
    <div className={`app-shell${sidebarCollapsed ? " sidebar-collapsed" : ""}`}>
      <aside className="sidebar" ref={sidebarRef}>
        <div className="sidebar-header">
          <div className="sidebar-header-left">
            <button className="icon-button sidebar-collapse-button" title="Hide sidebar" onClick={closeSidebar}>
              «
            </button>
            <span className="brand">Markdown Hub</span>
          </div>
          <button
            className="link-button"
            onClick={() =>
              void api
                .logout()
                .catch(() => {})
                .then(() => {
                  logout();
                  setAuthenticated(false);
                })
            }
          >
            Sign out
          </button>
        </div>
        <SearchBar onSelect={openPage} />
        <FileTree
          onOpen={openPage}
          activePath={activePath}
          onActiveDeleted={() => {
            const previous = previousActivePathRef.current;
            if (previous && previous !== activePath) {
              openPage(previous);
            } else {
              navigate("/");
            }
          }}
          defaultFolderPath={defaultFolderPath}
          onDefaultFolderChanged={setDefaultFolderPath}
          reloadNonce={treeNonce}
        />
        <div className="sidebar-footer">
          <div className="theme-toggle">
            <button
              className={`icon-button ${themePreference === "light" ? "theme-toggle-active" : ""}`}
              title="Light mode"
              onClick={() => setThemePreference("light")}
            >
              ☀
            </button>
            <button
              className={`icon-button ${themePreference === "dark" ? "theme-toggle-active" : ""}`}
              title="Dark mode"
              onClick={() => setThemePreference("dark")}
            >
              ☾
            </button>
            <button
              className={`icon-button ${themePreference === "system" ? "theme-toggle-active" : ""}`}
              title="Match system"
              onClick={() => setThemePreference("system")}
            >
              🖥
            </button>
          </div>
          <button className="link-button" onClick={() => navigate("/account")}>
            Account
          </button>
          {isAdmin && (
            <button className="link-button" onClick={() => navigate("/admin")}>
              Admin
            </button>
          )}
          
          {isAdmin && (
            <button className="link-button" onClick={() => navigate("/ai-lab")}>
              AI
            </button>
          )}
        </div>
      </aside>
      {!sidebarCollapsed && <button className="mobile-backdrop" aria-label="Close sidebar" onClick={closeSidebar} />}
      <button className="icon-button sidebar-expand-button" title="Show sidebar" onClick={openSidebar}>
        »
      </button>
      <main className="content">
        <Routes>
          <Route path="/" element={<Welcome username={username} />} />
          <Route
            path="/page/*"
            element={<PageView key={reloadNonce} onPageCreated={() => setTreeNonce((n) => n + 1)} />}
          />
          <Route path="/account" element={<Account />} />
          {isAdmin && <Route path="/admin" element={<Admin />} />}
          {isAdmin && <Route path="/admin/activity" element={<ActivityLogPage />} />}
          {isAdmin && <Route path="/ai-lab" element={<AiLab />} />}
        </Routes>
      </main>
      {!assistantCollapsed && (
        <button className="mobile-backdrop" aria-label="Close AI assistant" onClick={closeAssistant} />
      )}
      <AiAssistantPanel
        currentPagePath={activePath}
        collapsed={assistantCollapsed}
        onCollapsedChange={(collapsed) => (collapsed ? closeAssistant() : openAssistant())}
        onContentAddedToCurrentPage={() => setReloadNonce((n) => n + 1)}
      />
    </div>
  );
}
