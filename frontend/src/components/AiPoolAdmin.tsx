import { generatorHeadline } from "./pools/poolPresentation";
import { useGenerationPools } from "./pools/useGenerationPools";

/** Maps a server status label to its pill styling. Unknown labels fall back to neutral. */
const STATUS_CLASS: Record<string, string> = {
  Generating: "admin-badge-active",
  Queued: "admin-badge",
  Full: "admin-badge-ok",
  Paused: "admin-badge-warn",
  Waiting: "admin-badge-warn",
  Off: "admin-badge-muted",
};

export function AiPoolAdmin() {
  const {
    status,
    settingsForm,
    setSettingsForm,
    pools,
    entries,
    selected,
    poolForm,
    setPoolForm,
    busy,
    error,
    notice,
    selectPool,
    startNewPool,
    closeEditor,
    savePool,
    deletePool,
    generateOne,
    forget,
    saveSettings,
  } = useGenerationPools();

  const headline = generatorHeadline(status);

  return (
    <section className="admin-section">
      <h2>AI generation pools</h2>
      <p className="muted admin-pool-intro">
        A pool pre-generates content for one kind of template placeholder in the background, so filling it is
        instant instead of waiting on the model. A template opts in by adding <code>- Pool: Name</code> to that
        placeholder in its <code>ai-template</code> block; the pool's prompt then replaces the template's own rules
        for it. Pool entries are written without knowledge of the rest of the page, so pools suit self-contained
        items (an interactible, an NPC name) rather than sections that must match their context.
      </p>

      {error && <div className="banner banner-error">{error}</div>}
      {notice && <div className="banner">{notice}</div>}

      {settingsForm && status && (
        <div className="admin-pool-panel">
          <div className="admin-pool-generator" title={status.reason}>
            <span className={`admin-pool-dot admin-pool-dot-${headline.tone}`} aria-hidden="true" />
            <div className="admin-pool-generator-text">
              <strong>{headline.text}</strong>
              <span className="muted">{status.reason}</span>
            </div>
            <button
              className="secondary"
              disabled={busy}
              onClick={() => saveSettings({ ...settingsForm, paused: !status.settings.paused })}
            >
              {status.settings.paused ? "Resume" : "Pause"}
            </button>
          </div>

          <div className="admin-pool-settings-grid">
            <label>
              Allowed from (UTC)
              <input
                type="time"
                value={settingsForm.windowStartUtc ?? ""}
                onChange={(e) => setSettingsForm({ ...settingsForm, windowStartUtc: e.target.value || null })}
              />
            </label>
            <label>
              Allowed until (UTC)
              <input
                type="time"
                value={settingsForm.windowEndUtc ?? ""}
                onChange={(e) => setSettingsForm({ ...settingsForm, windowEndUtc: e.target.value || null })}
              />
            </label>
            <label>
              Seconds between entries
              <input
                type="number"
                min={10}
                value={settingsForm.intervalSeconds}
                onChange={(e) => setSettingsForm({ ...settingsForm, intervalSeconds: Number(e.target.value) })}
              />
            </label>
            <label>
              Keep used entries (days)
              <input
                type="number"
                min={0}
                value={settingsForm.usedEntryRetentionDays}
                onChange={(e) => setSettingsForm({ ...settingsForm, usedEntryRetentionDays: Number(e.target.value) })}
              />
            </label>
            <div className="admin-pool-settings-actions">
              <button disabled={busy} onClick={() => saveSettings(settingsForm)}>
                Save schedule
              </button>
            </div>
          </div>
          <p className="muted admin-pool-hint">
            Server time is {status.nowUtc} UTC. Leave both times blank to allow generation at any hour; an end
            earlier than the start wraps past midnight. Used entries are kept only so the same text isn't generated
            twice - forgotten entries are kept forever, which is what makes forgetting permanent.
          </p>
        </div>
      )}

      {!pools ? (
        <p className="muted">Loading…</p>
      ) : pools.length === 0 ? (
        <p className="muted">No pools yet. Create one to start pre-generating template content.</p>
      ) : (
        <table className="admin-table admin-pool-table">
          <thead>
            <tr>
              <th>Pool</th>
              <th>Ready</th>
              <th>Status</th>
              <th />
            </tr>
          </thead>
          <tbody>
            {pools.map((pool) => (
              <tr key={pool.id} className={selected === pool.id ? "admin-pool-row-selected" : undefined}>
                <td className="admin-pool-name">{pool.name}</td>
                <td>
                  <div className="admin-pool-progress" title={`${pool.readyCount} of ${pool.targetCount} ready`}>
                    <div className="admin-pool-progress-track">
                      <div
                        className={`admin-pool-progress-fill${pool.status === "Generating" ? " admin-pool-progress-busy" : ""}`}
                        style={{ width: `${pool.targetCount === 0 ? 0 : Math.min(100, (pool.readyCount / pool.targetCount) * 100)}%` }}
                      />
                    </div>
                    <span className="admin-pool-progress-count">
                      {pool.readyCount} / {pool.targetCount}
                    </span>
                  </div>
                </td>
                <td>
                  {/* The reason is the whole point - "Generating: No" with no explanation was the complaint. */}
                  <span className={`admin-badge ${STATUS_CLASS[pool.status] ?? "admin-badge"}`} title={pool.statusReason}>
                    {pool.status === "Generating" && <span className="admin-pool-spinner" aria-hidden="true" />}
                    {pool.status}
                  </span>
                </td>
                <td>
                  <div className="admin-actions">
                    <button className="secondary" disabled={busy} onClick={() => selectPool(pool)}>
                      Edit
                    </button>
                    <button className="secondary" disabled={busy} onClick={() => deletePool(pool)}>
                      Delete
                    </button>
                  </div>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      )}

      {selected === null && (
        <button className="secondary admin-pool-new" disabled={busy} onClick={startNewPool}>
          New pool
        </button>
      )}

      {selected !== null && (
        <div className="admin-pool-panel admin-pool-editor">
          <div className="admin-pool-editor-header">
            <h3>{selected === "new" ? "New pool" : `Pool “${poolForm.name}”`}</h3>
            <button className="link-button" onClick={closeEditor}>
              Close
            </button>
          </div>

          <div className="admin-pool-settings-grid">
            {selected === "new" && (
              <label>
                Name
                <input
                  value={poolForm.name}
                  placeholder="Interactible"
                  onChange={(e) => setPoolForm({ ...poolForm, name: e.target.value })}
                />
              </label>
            )}
            <label>
              Entries to keep ready
              <input
                type="number"
                min={0}
                value={poolForm.targetCount}
                onChange={(e) => setPoolForm({ ...poolForm, targetCount: Number(e.target.value) })}
              />
            </label>
            <label className="admin-pool-toggle">
              <input
                type="checkbox"
                checked={poolForm.enabled}
                onChange={(e) => setPoolForm({ ...poolForm, enabled: e.target.checked })}
              />
              Generate entries for this pool in the background
            </label>
          </div>

          <label className="admin-pool-prompt">
            <span>
              Prompt - the same bullet rules a template's <code>ai-template</code> block uses, including{" "}
              <code>Format:</code>, <code>Example:</code>, <code>Max words:</code>, and <code>Max sentences:</code>.
            </span>
            <textarea
              aria-label="Pool prompt"
              rows={8}
              value={poolForm.instructions}
              onChange={(e) => setPoolForm({ ...poolForm, instructions: e.target.value })}
            />
          </label>

          <div className="admin-pool-editor-actions">
            <button disabled={busy || !poolForm.name.trim()} onClick={() => savePool()}>
              Save pool
            </button>
            {selected !== "new" && (
              <button className="secondary" disabled={busy} onClick={() => generateOne(selected)}>
                Generate one now
              </button>
            )}
          </div>

          {selected !== "new" && (
            <div className="admin-pool-entries">
              <h4>Ready entries ({entries.length})</h4>
              {entries.length === 0 ? (
                <p className="muted">Nothing generated yet.</p>
              ) : (
                entries.map((entry) => (
                  <div key={entry.id} className="admin-pool-entry">
                    <span>{entry.content}</span>
                    <button
                      className="secondary"
                      disabled={busy}
                      title="Forget this entry - never show or regenerate it"
                      onClick={() => forget(entry.id)}
                    >
                      Forget
                    </button>
                  </div>
                ))
              )}
            </div>
          )}
        </div>
      )}
    </section>
  );
}
