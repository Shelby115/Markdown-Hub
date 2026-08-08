import { GenerationPool } from "../../api/client";
import { GenerationPoolsController } from "./useGenerationPools";
import { generatorHeadline, percentFull, toneOf } from "./poolPresentation";

/**
 * Variant B - "cards". Each pool is a tile with a progress ring, so fill level reads at a glance
 * from across the room. Editing opens a focused modal rather than pushing the page around.
 */
export function PoolsCards({ c }: { c: GenerationPoolsController }) {
  const headline = generatorHeadline(c.status);

  return (
    <div className="pv pv-cards">
      <header className="pv-cards-header">
        <div className={`pv-cards-status pv-cards-status-${headline.tone}`}>
          <span className={`pv-led pv-led-${headline.tone}`} aria-hidden="true" />
          <div>
            <strong>{headline.text}</strong>
            <p>{c.status?.reason}</p>
          </div>
          <button className="secondary" disabled={c.busy} onClick={c.togglePause}>
            {c.status?.settings.paused ? "Resume" : "Pause"}
          </button>
        </div>

        {c.settingsForm && (
          <details className="pv-cards-schedule">
            <summary>
              Schedule
              <span className="muted">
                {c.settingsForm.windowStartUtc && c.settingsForm.windowEndUtc
                  ? `${c.settingsForm.windowStartUtc}-${c.settingsForm.windowEndUtc} UTC`
                  : "any hour"}
                , every {c.settingsForm.intervalSeconds}s · server clock {c.status?.nowUtc} UTC
              </span>
            </summary>
            <div className="pv-cards-schedule-form">
              <label>
                Allowed from (UTC)
                <input
                  type="time"
                  value={c.settingsForm.windowStartUtc ?? ""}
                  onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, windowStartUtc: e.target.value || null })}
                />
              </label>
              <label>
                Allowed until (UTC)
                <input
                  type="time"
                  value={c.settingsForm.windowEndUtc ?? ""}
                  onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, windowEndUtc: e.target.value || null })}
                />
              </label>
              <label>
                Seconds between entries
                <input
                  type="number"
                  min={10}
                  value={c.settingsForm.intervalSeconds}
                  onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, intervalSeconds: Number(e.target.value) })}
                />
              </label>
              <label>
                Keep used entries (days)
                <input
                  type="number"
                  min={0}
                  value={c.settingsForm.usedEntryRetentionDays}
                  onChange={(e) =>
                    c.setSettingsForm({ ...c.settingsForm!, usedEntryRetentionDays: Number(e.target.value) })
                  }
                />
              </label>
              <button disabled={c.busy} onClick={() => c.saveSettings(c.settingsForm!)}>
                Save schedule
              </button>
            </div>
          </details>
        )}
      </header>

      {c.error && <div className="banner banner-error">{c.error}</div>}
      {c.notice && <div className="banner">{c.notice}</div>}

      <div className="pv-cards-grid">
        {(c.pools ?? []).map((pool) => (
          <PoolCard key={pool.id} pool={pool} busy={c.busy} onEdit={() => c.selectPool(pool)} onDelete={() => c.deletePool(pool)} />
        ))}
        <button className="pv-cards-add" onClick={c.startNewPool}>
          <span aria-hidden="true">+</span>
          New pool
        </button>
      </div>

      {c.selected !== null && (
        <div className="modal-overlay" onClick={c.busy ? undefined : c.closeEditor}>
          <div className="modal pv-cards-modal" onClick={(e) => e.stopPropagation()}>
            <h2>{c.selected === "new" ? "New pool" : c.poolForm.name}</h2>

            <div className="pv-cards-modal-form">
              {c.selected === "new" && (
                <label>
                  Name
                  <input
                    value={c.poolForm.name}
                    placeholder="Interactible"
                    onChange={(e) => c.setPoolForm({ ...c.poolForm, name: e.target.value })}
                  />
                </label>
              )}
              <label>
                Entries to keep ready
                <input
                  type="number"
                  min={0}
                  value={c.poolForm.targetCount}
                  onChange={(e) => c.setPoolForm({ ...c.poolForm, targetCount: Number(e.target.value) })}
                />
              </label>
              <label className="pv-cards-switch">
                <input
                  type="checkbox"
                  checked={c.poolForm.enabled}
                  onChange={(e) => c.setPoolForm({ ...c.poolForm, enabled: e.target.checked })}
                />
                <span />
                Generate in the background
              </label>
            </div>

            <label className="pv-cards-prompt">
              Prompt
              <textarea
                aria-label="Pool prompt"
                rows={7}
                value={c.poolForm.instructions}
                onChange={(e) => c.setPoolForm({ ...c.poolForm, instructions: e.target.value })}
              />
            </label>

            {c.selected !== "new" && (
              <div className="pv-cards-entries">
                <h3>Ready entries ({c.entries.length})</h3>
                {c.entries.length === 0 ? (
                  <p className="muted">Nothing generated yet.</p>
                ) : (
                  c.entries.map((entry) => (
                    <div key={entry.id} className="pv-cards-entry">
                      <span>{entry.content}</span>
                      <button className="link-button" disabled={c.busy} onClick={() => c.forget(entry.id)}>
                        Forget
                      </button>
                    </div>
                  ))
                )}
              </div>
            )}

            <div className="modal-actions">
              <button className="secondary" onClick={c.closeEditor}>
                Close
              </button>
              {c.selected !== "new" && (
                <button className="secondary" disabled={c.busy} onClick={() => c.generateOne(c.selected as number)}>
                  Generate one now
                </button>
              )}
              <button disabled={c.busy || !c.poolForm.name.trim()} onClick={c.savePool}>
                Save pool
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}

function PoolCard({
  pool,
  busy,
  onEdit,
  onDelete,
}: {
  pool: GenerationPool;
  busy: boolean;
  onEdit: () => void;
  onDelete: () => void;
}) {
  const percent = percentFull(pool);
  const tone = toneOf(pool.status);
  // 2πr for r=26, so the dash offset can be driven straight off the percentage.
  const circumference = 163.4;

  return (
    <article className={`pv-cards-card pv-cards-card-${tone}`}>
      <div className="pv-cards-ring">
        <svg viewBox="0 0 60 60" aria-hidden="true">
          <circle cx="30" cy="30" r="26" className="pv-cards-ring-track" />
          <circle
            cx="30"
            cy="30"
            r="26"
            className={`pv-cards-ring-fill${pool.status === "Generating" ? " pv-cards-ring-busy" : ""}`}
            strokeDasharray={circumference}
            strokeDashoffset={circumference - (circumference * percent) / 100}
          />
        </svg>
        <span className="pv-cards-ring-label">{percent}%</span>
      </div>
      <div className="pv-cards-card-body">
        <h3>{pool.name}</h3>
        <p className="pv-cards-card-count">
          {pool.readyCount} of {pool.targetCount} ready
        </p>
        <span className={`pv-tag pv-tag-${tone}`} title={pool.statusReason}>
          {pool.status}
        </span>
      </div>
      <div className="pv-cards-card-actions">
        <button className="secondary" disabled={busy} onClick={onEdit}>
          Edit
        </button>
        <button className="link-button" disabled={busy} onClick={onDelete}>
          Delete
        </button>
      </div>
    </article>
  );
}
