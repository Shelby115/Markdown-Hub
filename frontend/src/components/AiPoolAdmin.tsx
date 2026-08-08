import { useEffect, useState } from "react";
import {
  GenerationPool,
  GenerationPoolEntry,
  GenerationPoolSettings,
  GenerationPoolStatus,
  api,
  extractErrorMessage,
} from "../api/client";

interface PoolForm {
  name: string;
  instructions: string;
  targetCount: number;
  enabled: boolean;
}

const BLANK_POOL: PoolForm = {
  name: "",
  instructions: "- Describe what one entry should be.\n- Max words: 40\n",
  targetCount: 20,
  enabled: false,
};

/** "new" means the form is creating a pool rather than editing an existing one. */
type Selection = number | "new" | null;

export function AiPoolAdmin() {
  const [status, setStatus] = useState<GenerationPoolStatus | null>(null);
  const [settingsForm, setSettingsForm] = useState<GenerationPoolSettings | null>(null);
  const [pools, setPools] = useState<GenerationPool[] | null>(null);
  const [selected, setSelected] = useState<Selection>(null);
  const [poolForm, setPoolForm] = useState<PoolForm>(BLANK_POOL);
  const [entries, setEntries] = useState<GenerationPoolEntry[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  const loadStatus = async () => {
    const loaded = await api.adminGetPoolSettings();
    setStatus(loaded);
    setSettingsForm(loaded.settings);
  };

  const loadPools = async () => setPools(await api.adminGetPools());

  useEffect(() => {
    void loadStatus().catch((err) => setError(extractErrorMessage(err, "Could not load generator settings.")));
    void loadPools().catch((err) => setError(extractErrorMessage(err, "Could not load pools.")));
  }, []);

  /** Wraps an action with the shared busy/error handling every button here needs. */
  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
    } catch (err) {
      setError(extractErrorMessage(err, "That didn't work."));
    } finally {
      setBusy(false);
    }
  };

  const saveSettings = (settings: GenerationPoolSettings) =>
    run(async () => {
      const saved = await api.adminSetPoolSettings(settings);
      setStatus(saved);
      setSettingsForm(saved.settings);
    });

  const selectPool = (pool: GenerationPool) =>
    run(async () => {
      setSelected(pool.id);
      setPoolForm({
        name: pool.name,
        instructions: pool.instructions,
        targetCount: pool.targetCount,
        enabled: pool.enabled,
      });
      setEntries(await api.adminGetPoolEntries(pool.id));
    });

  const startNewPool = () => {
    setSelected("new");
    setPoolForm(BLANK_POOL);
    setEntries([]);
    setError(null);
    setNotice(null);
  };

  const savePool = () =>
    run(async () => {
      const saved =
        selected === "new"
          ? await api.adminCreatePool(poolForm)
          : await api.adminUpdatePool(selected as number, poolForm);
      await loadPools();
      setSelected(saved.id);
      setEntries(await api.adminGetPoolEntries(saved.id));
    });

  const deletePool = (pool: GenerationPool) =>
    run(async () => {
      if (!window.confirm(`Delete the “${pool.name}” pool and everything in it?`)) return;
      await api.adminDeletePool(pool.id);
      setSelected(null);
      setEntries([]);
      await loadPools();
    });

  const generateOne = (poolId: number) =>
    run(async () => {
      const entry = await api.adminGeneratePoolEntry(poolId);
      setEntries((prev) => [entry, ...prev]);
      setNotice("Added one entry.");
      await loadPools();
    });

  const forget = (entryId: number) =>
    run(async () => {
      await api.aiPoolForgetEntry(entryId);
      setEntries((prev) => prev.filter((e) => e.id !== entryId));
      await loadPools();
    });

  const generatorState = !status
    ? "…"
    : status.settings.paused
      ? "Paused"
      : status.runningNow
        ? "Running"
        : "Idle - outside the allowed window";

  return (
    <section className="admin-section">
      <h2>AI generation pools</h2>
      <p className="muted">
        A pool pre-generates content for one kind of template placeholder in the background, so filling it is
        instant instead of waiting on the model. A template opts in by adding <code>- Pool: Name</code> to that
        placeholder in its <code>ai-template</code> block; the pool's prompt below then replaces the template's own
        rules for it. Pool entries are written without knowledge of the rest of the page, so pools suit
        self-contained items (an interactible, an NPC name) rather than sections that must match their context.
      </p>

      {error && <div className="banner banner-error">{error}</div>}
      {notice && <div className="banner">{notice}</div>}

      {settingsForm && status && (
        <div className="admin-pool-settings">
          <p className="muted">
            Background generator: <strong>{generatorState}</strong>. Server time is {status.nowUtc} UTC.
          </p>
          <div className="admin-history-settings-form">
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
            <div>
              <button disabled={busy} onClick={() => void saveSettings(settingsForm)}>
                Save generator settings
              </button>
              <button
                className="secondary"
                disabled={busy}
                onClick={() => void saveSettings({ ...settingsForm, paused: !status.settings.paused })}
              >
                {status.settings.paused ? "Resume generating" : "Pause generating"}
              </button>
            </div>
          </div>
          <p className="muted">
            Leave both times blank to allow generation at any hour. Used entries are kept only so the same text
            isn't generated twice; forgotten entries are kept forever, which is what makes forgetting permanent.
          </p>
        </div>
      )}

      {!pools ? (
        <p className="muted">Loading…</p>
      ) : (
        <>
          <table className="admin-table">
            <thead>
              <tr>
                <th>Pool</th>
                <th>Ready</th>
                <th>Generating</th>
                <th />
              </tr>
            </thead>
            <tbody>
              {pools.length === 0 && (
                <tr>
                  <td colSpan={4} className="muted">
                    No pools yet.
                  </td>
                </tr>
              )}
              {pools.map((pool) => (
                <tr key={pool.id}>
                  <td>{pool.name}</td>
                  <td>
                    {pool.readyCount} / {pool.targetCount}
                  </td>
                  <td>{pool.enabled ? "Yes" : "No"}</td>
                  <td>
                    <button className="secondary" disabled={busy} onClick={() => void selectPool(pool)}>
                      Edit
                    </button>
                    <button className="secondary" disabled={busy} onClick={() => void deletePool(pool)}>
                      Delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>

          <button className="secondary" disabled={busy} onClick={startNewPool}>
            New pool
          </button>
        </>
      )}

      {selected !== null && (
        <div className="admin-pool-editor">
          <h3>{selected === "new" ? "New pool" : `Pool “${poolForm.name}”`}</h3>
          <div className="admin-history-settings-form">
            {selected === "new" && (
              <label>
                Name (must match the template's <code>Pool:</code> line)
                <input value={poolForm.name} onChange={(e) => setPoolForm({ ...poolForm, name: e.target.value })} />
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
            <label className="admin-pool-enabled">
              <input
                type="checkbox"
                checked={poolForm.enabled}
                onChange={(e) => setPoolForm({ ...poolForm, enabled: e.target.checked })}
              />
              Generate entries for this pool in the background
            </label>
          </div>

          <label className="admin-pool-prompt">
            Prompt - same bullet rules a template's <code>ai-template</code> block uses, including{" "}
            <code>Format:</code>, <code>Example:</code>, <code>Max words:</code>, and <code>Max sentences:</code>.
            <textarea
              aria-label="Pool prompt"
              rows={8}
              value={poolForm.instructions}
              onChange={(e) => setPoolForm({ ...poolForm, instructions: e.target.value })}
            />
          </label>

          <div>
            <button disabled={busy || !poolForm.name.trim()} onClick={() => void savePool()}>
              Save pool
            </button>
            {selected !== "new" && (
              <button className="secondary" disabled={busy} onClick={() => void generateOne(selected)}>
                Generate one now
              </button>
            )}
            <button className="secondary" disabled={busy} onClick={() => setSelected(null)}>
              Close
            </button>
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
                      onClick={() => void forget(entry.id)}
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
