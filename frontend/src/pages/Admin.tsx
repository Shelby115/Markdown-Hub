import { FormEvent, useEffect, useState } from "react";
import { Link } from "react-router-dom";
import {
  AdminFolderPermission,
  AdminUser,
  AiSettings,
  AuthenticationProvider,
  HistorySettings,
  PROVIDER_TYPE_LABELS,
  ProviderConfiguration,
  ProviderPreset,
  SaveAuthenticationProviderRequest,
  PERMISSION_LEVEL_LABELS,
  api,
  extractErrorMessage,
} from "../api/client";
import { AiPoolAdmin } from "../components/AiPoolAdmin";

const EMPTY_CONFIGURATION: ProviderConfiguration = {
  authority: "",
  requireHttpsMetadata: true,
  audience: "",
  authorizationEndpoint: "",
  tokenEndpoint: "",
  userInfoEndpoint: "",
  scopes: "openid profile email",
  userIdField: "sub",
  emailField: "email",
  nameField: "name",
  autoProvision: 0,
};

const EMPTY_PROVIDER_FORM: SaveAuthenticationProviderRequest = {
  name: "",
  displayName: "",
  type: 0,
  clientId: "",
  clientSecret: "",
  configuration: EMPTY_CONFIGURATION,
};

function formatDate(value: string | null) {
  if (!value) return "Never";
  return new Date(value).toLocaleString();
}

export function Admin() {
  const [users, setUsers] = useState<AdminUser[] | null>(null);
  const [permissions, setPermissions] = useState<AdminFolderPermission[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [busyUserId, setBusyUserId] = useState<number | null>(null);
  const [busyPermissionId, setBusyPermissionId] = useState<number | null>(null);

  const [grantUserId, setGrantUserId] = useState<number | "">("");
  const [grantFolderPath, setGrantFolderPath] = useState("");
  const [grantLevel, setGrantLevel] = useState(0);
  const [grantBusy, setGrantBusy] = useState(false);

  const [newUsername, setNewUsername] = useState("");
  const [newUserIsAdmin, setNewUserIsAdmin] = useState(false);
  const [createUserBusy, setCreateUserBusy] = useState(false);
  const [createdTempPassword, setCreatedTempPassword] = useState<{ username: string; password: string } | null>(null);
  const [resetPasswordUserId, setResetPasswordUserId] = useState<number | null>(null);
  const [resetPasswordValue, setResetPasswordValue] = useState("");
  const [resetPasswordBusy, setResetPasswordBusy] = useState(false);

  const [aiSettings, setAiSettings] = useState<AiSettings | null>(null);
  const [aiModels, setAiModels] = useState<string[] | null>(null);
  const [aiModelsError, setAiModelsError] = useState<string | null>(null);
  const [modelInput, setModelInput] = useState("");
  const [aiBusy, setAiBusy] = useState(false);

  const [historySettings, setHistorySettings] = useState<HistorySettings | null>(null);
  const [historyForm, setHistoryForm] = useState<HistorySettings | null>(null);
  const [historyBusy, setHistoryBusy] = useState(false);

  const [providers, setProviders] = useState<AuthenticationProvider[] | null>(null);
  const [presets, setPresets] = useState<ProviderPreset[]>([]);
  const [providerForm, setProviderForm] = useState<SaveAuthenticationProviderRequest>(EMPTY_PROVIDER_FORM);
  const [providerBusy, setProviderBusy] = useState(false);
  const [busyProviderId, setBusyProviderId] = useState<number | null>(null);

  const adminCount = users?.filter((u) => u.isAdministrator).length ?? 0;

  const load = async () => {
    try {
      const [u, p] = await Promise.all([api.adminListUsers(), api.adminListPermissions()]);
      setUsers(u);
      setPermissions(p);
    } catch {
      setError("Couldn't load admin data.");
    }
  };

  const loadAiSettings = async () => {
    try {
      const settings = await api.adminGetAiSettings();
      setAiSettings(settings);
      setModelInput(settings.selectedModel ?? "");
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't load AI settings."));
    }
    // Listing installed models can fail independently (e.g. Ollama unreachable) without
    // blocking the settings above - a manual model name can still be typed in either way.
    try {
      const { models } = await api.adminListAiModels();
      setAiModels(models);
      setAiModelsError(null);
    } catch (err) {
      setAiModelsError(extractErrorMessage(err, "Couldn't list installed Ollama models."));
    }
  };

  const loadHistorySettings = async () => {
    try {
      const settings = await api.adminGetHistorySettings();
      setHistorySettings(settings);
      setHistoryForm(settings);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't load version history / activity log settings."));
    }
  };

  const loadProviders = async () => {
    try {
      setProviders(await api.adminListAuthProviders());
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't load authentication providers."));
    }
    try {
      setPresets(await api.adminGetProviderPresets());
    } catch {
      setPresets([]);
    }
  };

  useEffect(() => {
    void load();
    void loadAiSettings();
    void loadHistorySettings();
    void loadProviders();
  }, []);

  const toggleDisabled = async (user: AdminUser) => {
    setBusyUserId(user.id);
    try {
      await api.adminSetUserDisabled(user.id, !user.isDisabled);
      setUsers((prev) => prev?.map((u) => (u.id === user.id ? { ...u, isDisabled: !user.isDisabled } : u)) ?? null);
    } catch {
      setError("Couldn't update that user.");
    } finally {
      setBusyUserId(null);
    }
  };

  const toggleRole = async (user: AdminUser) => {
    setBusyUserId(user.id);
    setError(null);
    try {
      await api.adminSetUserRole(user.id, !user.isAdministrator);
      setUsers((prev) => prev?.map((u) => (u.id === user.id ? { ...u, isAdministrator: !user.isAdministrator } : u)) ?? null);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't update that user's role."));
    } finally {
      setBusyUserId(null);
    }
  };

  const revokePermission = async (permission: AdminFolderPermission) => {
    setBusyPermissionId(permission.id);
    try {
      await api.adminRevokePermission(permission.id);
      setPermissions((prev) => prev?.filter((p) => p.id !== permission.id) ?? null);
    } catch {
      setError("Couldn't revoke that permission.");
    } finally {
      setBusyPermissionId(null);
    }
  };

  const submitCreateUser = async (event: FormEvent) => {
    event.preventDefault();
    const username = newUsername.trim();
    if (!username) return;
    setCreateUserBusy(true);
    setError(null);
    try {
      const created = await api.adminCreateUser(username, newUserIsAdmin);
      setUsers(await api.adminListUsers());
      setCreatedTempPassword({ username: created.username, password: created.temporaryPassword });
      setNewUsername("");
      setNewUserIsAdmin(false);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't create that user."));
    } finally {
      setCreateUserBusy(false);
    }
  };

  const submitResetPassword = async (event: FormEvent) => {
    event.preventDefault();
    if (resetPasswordUserId === null || resetPasswordValue.length < 8) return;
    setResetPasswordBusy(true);
    setError(null);
    try {
      await api.adminSetUserPassword(resetPasswordUserId, resetPasswordValue);
      setUsers(await api.adminListUsers());
      setResetPasswordUserId(null);
      setResetPasswordValue("");
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't reset that user's password."));
    } finally {
      setResetPasswordBusy(false);
    }
  };

  const saveAiModel = async () => {
    setAiBusy(true);
    setError(null);
    try {
      const settings = await api.adminSetAiModel(modelInput.trim() || null);
      setAiSettings(settings);
      setModelInput(settings.selectedModel ?? "");
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't save the AI model."));
    } finally {
      setAiBusy(false);
    }
  };

  const resetAiModel = async () => {
    setAiBusy(true);
    setError(null);
    try {
      const settings = await api.adminSetAiModel(null);
      setAiSettings(settings);
      setModelInput("");
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't reset the AI model."));
    } finally {
      setAiBusy(false);
    }
  };

  const saveHistorySettings = async () => {
    if (!historyForm) return;
    setHistoryBusy(true);
    setError(null);
    try {
      const settings = await api.adminSetHistorySettings(historyForm);
      setHistorySettings(settings);
      setHistoryForm(settings);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't save those retention settings."));
    } finally {
      setHistoryBusy(false);
    }
  };

  const applyPreset = (preset: ProviderPreset) => {
    setProviderForm({
      name: preset.key,
      displayName: preset.displayName,
      type: preset.type,
      clientId: "",
      clientSecret: "",
      configuration: preset.configuration,
    });
  };

  const submitCreateProvider = async (event: FormEvent) => {
    event.preventDefault();
    setProviderBusy(true);
    setError(null);
    try {
      const created = await api.adminCreateAuthProvider(providerForm);
      setProviders((prev) => (prev ? [...prev, created] : [created]));
      setProviderForm(EMPTY_PROVIDER_FORM);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't add that provider."));
    } finally {
      setProviderBusy(false);
    }
  };

  const toggleProviderEnabled = async (provider: AuthenticationProvider) => {
    setBusyProviderId(provider.id);
    setError(null);
    try {
      const updated = provider.enabled
        ? await api.adminDisableAuthProvider(provider.id)
        : await api.adminEnableAuthProvider(provider.id);
      setProviders((prev) => prev?.map((p) => (p.id === provider.id ? updated : p)) ?? null);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't update that provider."));
    } finally {
      setBusyProviderId(null);
    }
  };

  const deleteProvider = async (provider: AuthenticationProvider) => {
    if (
      provider.usersUsingProvider > 0 &&
      !window.confirm(
        `This provider is currently used by ${provider.usersUsingProvider} user(s).\n\n` +
          `Removing it will prevent those users from signing in through this provider. Continue?`
      )
    ) {
      return;
    }
    setBusyProviderId(provider.id);
    setError(null);
    try {
      await api.adminDeleteAuthProvider(provider.id);
      setProviders((prev) => prev?.filter((p) => p.id !== provider.id) ?? null);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't delete that provider."));
    } finally {
      setBusyProviderId(null);
    }
  };

  const submitGrant = async (event: FormEvent) => {
    event.preventDefault();
    if (grantUserId === "") return;
    setGrantBusy(true);
    setError(null);
    try {
      await api.adminGrantPermission(grantUserId, grantFolderPath.trim(), grantLevel);
      setPermissions(await api.adminListPermissions());
      setGrantFolderPath("");
      setGrantLevel(0);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't grant that permission."));
    } finally {
      setGrantBusy(false);
    }
  };

  return (
    <div className="admin-page">
      <div className="admin-page-header">
        <h1>Admin</h1>
        <Link className="link-button" to="/admin/activity">
          Activity Log →
        </Link>
      </div>
      {error && <div className="banner banner-error">{error}</div>}

      <section className="admin-section">
        <h2>Users</h2>
        {!users ? (
          <p className="muted">Loading…</p>
        ) : (
          <table className="admin-table">
            <thead>
              <tr>
                <th>Username</th>
                <th>Email</th>
                <th>Role</th>
                <th>Status</th>
                <th>Created</th>
                <th>Last login</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {users.map((u) => {
                const isLastAdmin = u.isAdministrator && adminCount <= 1;
                return (
                  <tr key={u.id}>
                    <td>{u.username}</td>
                    <td>{u.email ?? "—"}</td>
                    <td>{u.isAdministrator ? <span className="admin-badge">Admin</span> : "User"}</td>
                    <td>
                      {u.isDisabled ? (
                        <span className="admin-badge admin-badge-warn">Disabled</span>
                      ) : !u.hasPassword && u.linkedIdentityCount === 0 ? (
                        <span className="admin-badge" title="Has no password or linked sign-in method yet">
                          No sign-in method
                        </span>
                      ) : (
                        <span className="admin-badge admin-badge-ok">Active</span>
                      )}
                    </td>
                    <td>{formatDate(u.createdAt)}</td>
                    <td>{formatDate(u.lastLoginAt)}</td>
                    <td className="admin-actions">
                      <button
                        className="secondary"
                        disabled={busyUserId === u.id || isLastAdmin}
                        title={isLastAdmin ? "At least one administrator is required" : undefined}
                        onClick={() => toggleRole(u)}
                      >
                        {u.isAdministrator ? "Demote" : "Promote"}
                      </button>
                      <button
                        className="secondary"
                        disabled={busyUserId === u.id || u.isAdministrator}
                        title={u.isAdministrator ? "Administrators can't be disabled" : undefined}
                        onClick={() => toggleDisabled(u)}
                      >
                        {u.isDisabled ? "Enable" : "Disable"}
                      </button>
                      <button
                        className="secondary"
                        onClick={() => {
                          setResetPasswordUserId(u.id);
                          setResetPasswordValue("");
                        }}
                      >
                        Set password
                      </button>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        )}

        {resetPasswordUserId !== null && (
          <form className="admin-grant-form" onSubmit={(e) => void submitResetPassword(e)}>
            <span className="muted">
              New password for {users?.find((u) => u.id === resetPasswordUserId)?.username}:
            </span>
            <input
              type="password"
              placeholder="New password (min 8 characters)"
              value={resetPasswordValue}
              onChange={(e) => setResetPasswordValue(e.target.value)}
            />
            <button type="submit" disabled={resetPasswordBusy || resetPasswordValue.length < 8}>
              Save
            </button>
            <button type="button" className="secondary" onClick={() => setResetPasswordUserId(null)}>
              Cancel
            </button>
          </form>
        )}

        {createdTempPassword && (
          <div className="banner banner-warning">
            Temporary password for <strong>{createdTempPassword.username}</strong>: <code>{createdTempPassword.password}</code>{" "}
            — shown once, share it with them now.{" "}
            <button className="link-button" onClick={() => setCreatedTempPassword(null)}>
              Dismiss
            </button>
          </div>
        )}

        <form className="admin-grant-form" onSubmit={submitCreateUser}>
          <input
            type="text"
            placeholder="Username"
            value={newUsername}
            onChange={(e) => setNewUsername(e.target.value)}
          />
          <label className="template-toggle">
            <input type="checkbox" checked={newUserIsAdmin} onChange={(e) => setNewUserIsAdmin(e.target.checked)} />
            Administrator
          </label>
          <button type="submit" disabled={createUserBusy || !newUsername.trim()}>
            Add user (generates a temporary password)
          </button>
        </form>
      </section>

      <section className="admin-section">
        <h2>Authentication providers</h2>
        {!providers ? (
          <p className="muted">Loading…</p>
        ) : (
          <>
            <p className="muted">
              Optional external sign-in methods. Local username/password always works regardless of what&apos;s
              configured here - removing every provider below never locks anyone out.
            </p>
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Type</th>
                  <th>Client ID</th>
                  <th>Status</th>
                  <th>Users</th>
                  <th></th>
                </tr>
              </thead>
              <tbody>
                {providers.map((p) => (
                  <tr key={p.id}>
                    <td>{p.displayName}</td>
                    <td>{PROVIDER_TYPE_LABELS[p.type] ?? p.type}</td>
                    <td>{p.clientId}</td>
                    <td>
                      {p.enabled ? (
                        <span className="admin-badge admin-badge-ok">Enabled</span>
                      ) : (
                        <span className="admin-badge admin-badge-warn">Disabled</span>
                      )}
                      {!p.hasClientSecret && (
                        <span className="admin-badge admin-badge-warn" title="No client secret configured yet - sign-in through this provider will fail">
                          No secret
                        </span>
                      )}
                    </td>
                    <td>{p.usersUsingProvider}</td>
                    <td className="admin-actions">
                      <button className="secondary" disabled={busyProviderId === p.id} onClick={() => void toggleProviderEnabled(p)}>
                        {p.enabled ? "Disable" : "Enable"}
                      </button>
                      <button className="secondary" disabled={busyProviderId === p.id} onClick={() => void deleteProvider(p)}>
                        Delete
                      </button>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>

            {presets.length > 0 && (
              <div className="admin-grant-form">
                <span className="muted">Presets:</span>
                {presets.map((preset) => (
                  <button key={preset.key} type="button" className="secondary" onClick={() => applyPreset(preset)}>
                    {preset.displayName}
                  </button>
                ))}
              </div>
            )}

            <form className="admin-grant-form" onSubmit={(e) => void submitCreateProvider(e)}>
              <input
                type="text"
                placeholder="Internal name (e.g. keycloak)"
                value={providerForm.name ?? ""}
                onChange={(e) => setProviderForm({ ...providerForm, name: e.target.value })}
              />
              <input
                type="text"
                placeholder="Display name (e.g. Keycloak)"
                value={providerForm.displayName}
                onChange={(e) => setProviderForm({ ...providerForm, displayName: e.target.value })}
              />
              <select value={providerForm.type} onChange={(e) => setProviderForm({ ...providerForm, type: Number(e.target.value) })}>
                {PROVIDER_TYPE_LABELS.map((label, i) => (
                  <option key={label} value={i}>
                    {label}
                  </option>
                ))}
              </select>
              <input
                type="text"
                placeholder="Client ID"
                value={providerForm.clientId}
                onChange={(e) => setProviderForm({ ...providerForm, clientId: e.target.value })}
              />
              <input
                type="password"
                placeholder="Client secret"
                value={providerForm.clientSecret ?? ""}
                onChange={(e) => setProviderForm({ ...providerForm, clientSecret: e.target.value })}
              />

              {providerForm.type === 0 ? (
                <>
                  <input
                    type="text"
                    placeholder="Authority (issuer URL)"
                    value={providerForm.configuration.authority ?? ""}
                    onChange={(e) =>
                      setProviderForm({ ...providerForm, configuration: { ...providerForm.configuration, authority: e.target.value } })
                    }
                  />
                  <input
                    type="text"
                    placeholder="Audience (optional)"
                    value={providerForm.configuration.audience ?? ""}
                    onChange={(e) =>
                      setProviderForm({ ...providerForm, configuration: { ...providerForm.configuration, audience: e.target.value } })
                    }
                  />
                  <label className="template-toggle">
                    <input
                      type="checkbox"
                      checked={providerForm.configuration.requireHttpsMetadata}
                      onChange={(e) =>
                        setProviderForm({
                          ...providerForm,
                          configuration: { ...providerForm.configuration, requireHttpsMetadata: e.target.checked },
                        })
                      }
                    />
                    Require HTTPS
                  </label>
                </>
              ) : (
                <>
                  <input
                    type="text"
                    placeholder="Authorization endpoint"
                    value={providerForm.configuration.authorizationEndpoint ?? ""}
                    onChange={(e) =>
                      setProviderForm({
                        ...providerForm,
                        configuration: { ...providerForm.configuration, authorizationEndpoint: e.target.value },
                      })
                    }
                  />
                  <input
                    type="text"
                    placeholder="Token endpoint"
                    value={providerForm.configuration.tokenEndpoint ?? ""}
                    onChange={(e) =>
                      setProviderForm({ ...providerForm, configuration: { ...providerForm.configuration, tokenEndpoint: e.target.value } })
                    }
                  />
                  <input
                    type="text"
                    placeholder="Userinfo endpoint"
                    value={providerForm.configuration.userInfoEndpoint ?? ""}
                    onChange={(e) =>
                      setProviderForm({
                        ...providerForm,
                        configuration: { ...providerForm.configuration, userInfoEndpoint: e.target.value },
                      })
                    }
                  />
                </>
              )}
              <input
                type="text"
                placeholder="Scopes"
                value={providerForm.configuration.scopes}
                onChange={(e) =>
                  setProviderForm({ ...providerForm, configuration: { ...providerForm.configuration, scopes: e.target.value } })
                }
              />

              <button
                type="submit"
                disabled={providerBusy || !providerForm.name?.trim() || !providerForm.displayName.trim() || !providerForm.clientId.trim()}
              >
                Add provider
              </button>
            </form>
          </>
        )}
      </section>

      <section className="admin-section">
        <h2>AI model</h2>
        {!aiSettings ? (
          <p className="muted">Loading…</p>
        ) : (
          <>
            <p className="muted">
              Currently using <strong>{aiSettings.effectiveModel}</strong>
              {!aiSettings.selectedModel && " (configured default - no override set)"}. Applies to AI-assisted
              editing and the AI assistant for every user.
            </p>
            {aiModelsError && (
              <div className="banner banner-warning">{aiModelsError} You can still type a model name manually below.</div>
            )}
            <div className="admin-grant-form">
              <input
                list="ai-model-options"
                type="text"
                placeholder={aiSettings.configuredDefaultModel}
                value={modelInput}
                onChange={(e) => setModelInput(e.target.value)}
              />
              {aiModels && (
                <datalist id="ai-model-options">
                  {aiModels.map((m) => (
                    <option key={m} value={m} />
                  ))}
                </datalist>
              )}
              <button disabled={aiBusy} onClick={() => void saveAiModel()}>
                Save
              </button>
              <button className="secondary" disabled={aiBusy || !aiSettings.selectedModel} onClick={() => void resetAiModel()}>
                Reset to default
              </button>
            </div>
          </>
        )}
      </section>

      <AiPoolAdmin />

      <section className="admin-section">
        <h2>Version history &amp; activity log</h2>
        {!historyForm ? (
          <p className="muted">Loading…</p>
        ) : (
          <>
            <p className="muted">
              How long document versions and activity log entries are kept before being cleaned up, and how far
              back the activity log shows by default.
            </p>
            <div className="admin-history-settings-form">
              <label>
                Version history retention (days)
                <input
                  type="number"
                  min={0}
                  value={historyForm.versionRetentionDays}
                  onChange={(e) => setHistoryForm({ ...historyForm, versionRetentionDays: Number(e.target.value) })}
                />
              </label>
              <label>
                Activity log retention (days)
                <input
                  type="number"
                  min={0}
                  value={historyForm.activityRetentionDays}
                  onChange={(e) => setHistoryForm({ ...historyForm, activityRetentionDays: Number(e.target.value) })}
                />
              </label>
              <label>
                Activity log default view (days)
                <input
                  type="number"
                  min={0}
                  value={historyForm.activityDefaultDays}
                  onChange={(e) => setHistoryForm({ ...historyForm, activityDefaultDays: Number(e.target.value) })}
                />
              </label>
              <div>
                <button disabled={historyBusy} onClick={() => void saveHistorySettings()}>
                  Save retention settings
                </button>
                {historySettings && (
                  <button
                    className="secondary"
                    disabled={historyBusy}
                    onClick={() => setHistoryForm(historySettings)}
                  >
                    Revert changes
                  </button>
                )}
              </div>
            </div>
          </>
        )}
      </section>

      <section className="admin-section">
        <h2>Folder permissions</h2>
        {!permissions ? (
          <p className="muted">Loading…</p>
        ) : (
          <>
            {permissions.length === 0 ? (
              <p className="muted">No folder permissions have been granted yet.</p>
            ) : (
              <table className="admin-table">
                <thead>
                  <tr>
                    <th>User</th>
                    <th>Folder</th>
                    <th>Level</th>
                    <th></th>
                  </tr>
                </thead>
                <tbody>
                  {permissions.map((p) => (
                    <tr key={p.id}>
                      <td>{p.username}</td>
                      <td>{p.folderPath || "/ (hub root)"}</td>
                      <td>{PERMISSION_LEVEL_LABELS[p.level] ?? p.level}</td>
                      <td>
                        <button className="secondary" disabled={busyPermissionId === p.id} onClick={() => revokePermission(p)}>
                          Revoke
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            )}

            <form className="admin-grant-form" onSubmit={submitGrant}>
              <select
                value={grantUserId}
                onChange={(e) => setGrantUserId(e.target.value ? Number(e.target.value) : "")}
                required
              >
                <option value="">Select user…</option>
                {users?.map((u) => (
                  <option key={u.id} value={u.id}>
                    {u.username}
                  </option>
                ))}
              </select>
              <input
                type="text"
                placeholder="Folder path (blank = hub root)"
                value={grantFolderPath}
                onChange={(e) => setGrantFolderPath(e.target.value)}
              />
              <select value={grantLevel} onChange={(e) => setGrantLevel(Number(e.target.value))}>
                {PERMISSION_LEVEL_LABELS.map((label, i) => (
                  <option key={label} value={i}>
                    {label}
                  </option>
                ))}
              </select>
              <button type="submit" disabled={grantBusy || grantUserId === ""}>
                Grant
              </button>
            </form>
          </>
        )}
      </section>
    </div>
  );
}
