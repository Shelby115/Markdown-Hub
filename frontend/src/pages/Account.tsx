import { FormEvent, useEffect, useState } from "react";
import { useNavigate } from "react-router-dom";
import { AuthMethods, SessionInfo, api, extractErrorMessage } from "../api/client";
import { AuthProviderInfo, getProviders } from "../auth/auth";

function formatDate(value: string | null) {
  if (!value) return "Never";
  return new Date(value).toLocaleString();
}

/** Self-service account page (Auth.md §28): change/set a local password, add or remove linked
 * external providers, and review/revoke active sessions. Available to every signed-in user, not
 * just administrators - Admin.tsx covers managing *other* users and provider configuration. */
export function Account() {
  const navigate = useNavigate();
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<{ username: string; email: string | null } | null>(null);

  const [currentPassword, setCurrentPassword] = useState("");
  const [newPassword, setNewPassword] = useState("");
  const [confirmPassword, setConfirmPassword] = useState("");
  const [passwordBusy, setPasswordBusy] = useState(false);
  const [passwordMessage, setPasswordMessage] = useState<string | null>(null);

  const [authMethods, setAuthMethods] = useState<AuthMethods | null>(null);
  const [providers, setProviders] = useState<AuthProviderInfo[]>([]);
  const [busyId, setBusyId] = useState<number | string | null>(null);

  const [sessions, setSessions] = useState<SessionInfo[] | null>(null);

  const load = async () => {
    try {
      const [me, methods, sess, provs] = await Promise.all([
        api.getMe(),
        api.getAuthMethods(),
        api.getSessions(),
        getProviders(),
      ]);
      setInfo({ username: me.username, email: me.email });
      setAuthMethods(methods);
      setSessions(sess);
      setProviders(provs);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't load account information."));
    }
  };

  useEffect(() => {
    void load();
  }, []);

  const submitPasswordChange = async (event: FormEvent) => {
    event.preventDefault();
    setPasswordBusy(true);
    setError(null);
    setPasswordMessage(null);
    try {
      await api.changePassword(authMethods?.hasPassword ? currentPassword : null, newPassword, confirmPassword);
      setCurrentPassword("");
      setNewPassword("");
      setConfirmPassword("");
      setPasswordMessage("Password updated. Your other active sessions have been signed out.");
      await load();
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't change your password."));
    } finally {
      setPasswordBusy(false);
    }
  };

  const linkProvider = async (providerName: string) => {
    setError(null);
    try {
      const { redirectUrl } = await api.linkProviderStart(providerName);
      window.location.href = redirectUrl;
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't start linking that provider."));
    }
  };

  const removeMethod = async (id: number) => {
    setBusyId(id);
    setError(null);
    try {
      await api.removeAuthMethod(id);
      await load();
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't remove that sign-in method."));
    } finally {
      setBusyId(null);
    }
  };

  const revokeSession = async (id: string) => {
    setBusyId(id);
    setError(null);
    try {
      await api.revokeSession(id);
      setSessions((prev) => prev?.filter((s) => s.id !== id) ?? null);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't revoke that session."));
    } finally {
      setBusyId(null);
    }
  };

  const linkedProviderIds = new Set(authMethods?.linkedIdentities.map((i) => i.providerId) ?? []);
  const unlinkedProviders = providers.filter((p) => !linkedProviderIds.has(p.id));
  const usableMethodCount = (authMethods?.hasPassword ? 1 : 0) + (authMethods?.linkedIdentities.length ?? 0);

  return (
    <div className="admin-page">
      <div className="admin-page-header">
        <h1>Account</h1>
        <button className="link-button" onClick={() => navigate("/")}>
          ← Back to hub
        </button>
      </div>
      {error && <div className="banner banner-error">{error}</div>}

      <section className="admin-section">
        <h2>Profile</h2>
        {!info ? (
          <p className="muted">Loading…</p>
        ) : (
          <p>
            Signed in as <strong>{info.username}</strong>
            {info.email ? ` (${info.email})` : ""}.
          </p>
        )}
      </section>

      <section className="admin-section">
        <h2>Password</h2>
        <form className="admin-history-settings-form" onSubmit={(e) => void submitPasswordChange(e)}>
          {authMethods?.hasPassword && (
            <label>
              Current password
              <input
                type="password"
                autoComplete="current-password"
                value={currentPassword}
                onChange={(e) => setCurrentPassword(e.target.value)}
              />
            </label>
          )}
          <label>
            New password
            <input
              type="password"
              autoComplete="new-password"
              value={newPassword}
              onChange={(e) => setNewPassword(e.target.value)}
            />
          </label>
          <label>
            Confirm new password
            <input
              type="password"
              autoComplete="new-password"
              value={confirmPassword}
              onChange={(e) => setConfirmPassword(e.target.value)}
            />
          </label>
          <div>
            <button type="submit" disabled={passwordBusy || !newPassword || newPassword !== confirmPassword}>
              {authMethods?.hasPassword ? "Change password" : "Set password"}
            </button>
          </div>
          {passwordMessage && <p className="muted">{passwordMessage}</p>}
        </form>
      </section>

      <section className="admin-section">
        <h2>Authentication methods</h2>
        {!authMethods ? (
          <p className="muted">Loading…</p>
        ) : (
          <table className="admin-table">
            <thead>
              <tr>
                <th>Method</th>
                <th>Status</th>
                <th>Last used</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              <tr>
                <td>Local password</td>
                <td>
                  {authMethods.hasPassword ? (
                    <span className="admin-badge admin-badge-ok">Connected</span>
                  ) : (
                    <span className="admin-badge">Not set</span>
                  )}
                </td>
                <td>—</td>
                <td></td>
              </tr>
              {authMethods.linkedIdentities.map((identity) => (
                <tr key={identity.id}>
                  <td>{identity.providerDisplayName}</td>
                  <td>
                    <span className="admin-badge admin-badge-ok">Connected</span>
                  </td>
                  <td>{formatDate(identity.lastUsedAt)}</td>
                  <td>
                    <button
                      className="secondary"
                      disabled={busyId === identity.id || usableMethodCount <= 1}
                      title={usableMethodCount <= 1 ? "This is your only remaining sign-in method" : undefined}
                      onClick={() => void removeMethod(identity.id)}
                    >
                      Remove
                    </button>
                  </td>
                </tr>
              ))}
              {unlinkedProviders.map((p) => (
                <tr key={p.id}>
                  <td>{p.displayName}</td>
                  <td>
                    <span className="admin-badge">Not connected</span>
                  </td>
                  <td>—</td>
                  <td>
                    <button className="secondary" onClick={() => void linkProvider(p.name)}>
                      Connect
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>

      <section className="admin-section">
        <h2>Active sessions</h2>
        {!sessions ? (
          <p className="muted">Loading…</p>
        ) : (
          <table className="admin-table">
            <thead>
              <tr>
                <th>Started</th>
                <th>Last active</th>
                <th>Expires</th>
                <th>IP</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {sessions.map((s) => (
                <tr key={s.id}>
                  <td>{formatDate(s.createdAt)}</td>
                  <td>
                    {formatDate(s.lastActivityAt)}
                    {s.isCurrent ? " (this session)" : ""}
                  </td>
                  <td>{formatDate(s.expiresAt)}</td>
                  <td>{s.ipAddress ?? "—"}</td>
                  <td>
                    {!s.isCurrent && (
                      <button className="secondary" disabled={busyId === s.id} onClick={() => void revokeSession(s.id)}>
                        Revoke
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </section>
    </div>
  );
}
