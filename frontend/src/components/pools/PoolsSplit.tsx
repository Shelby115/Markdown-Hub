import { GenerationPoolsController } from "./useGenerationPools";
import { generatorHeadline, percentFull, toneOf } from "./poolPresentation";

/**
 * Variant C - "settings app". A list rail on the left, the selected pool filling the right pane.
 * Nothing appears or disappears as you click around, so the page never jumps; the trade-off is
 * that you only ever see one pool's prompt and entries at a time.
 */
export function PoolsSplit({ c }: { c: GenerationPoolsController }) {
  const headline = generatorHeadline(c.status);
  const selectedPool = typeof c.selected === "number" ? c.pools?.find((p) => p.id === c.selected) : undefined;

  return (
    <div className="pv pv-split">
      <div className="pv-split-bar" title={c.status?.reason}>
        <span className={`pv-led pv-led-${headline.tone}`} aria-hidden="true" />
        <strong>{headline.text}</strong>
        <span className="muted">{c.status?.reason}</span>
        <button className="secondary" disabled={c.busy} onClick={c.togglePause}>
          {c.status?.settings.paused ? "Resume" : "Pause"}
        </button>
      </div>

      {c.error && <div className="banner banner-error">{c.error}</div>}
      {c.notice && <div className="banner">{c.notice}</div>}

      <div className="pv-split-body">
        <nav className="pv-split-rail">
          <h3>Pools</h3>
          <ul>
            {(c.pools ?? []).map((pool) => (
              <li key={pool.id}>
                <button
                  className={`pv-split-rail-item${c.selected === pool.id ? " pv-split-rail-item-active" : ""}`}
                  onClick={() => c.selectPool(pool)}
                >
                  <span className="pv-split-rail-name">
                    <span className={`pv-dot pv-dot-${toneOf(pool.status)}`} aria-hidden="true" />
                    {pool.name}
                  </span>
                  {/* The status has to be readable without selecting the pool first - otherwise
                      "why isn't this one filling?" needs a click to answer. */}
                  <span className={`pv-tag pv-tag-${toneOf(pool.status)}`} title={pool.statusReason}>
                    {pool.status}
                  </span>
                  <span className="pv-split-rail-meta">
                    <span className="pv-split-rail-bar">
                      <span style={{ width: `${percentFull(pool)}%` }} />
                    </span>
                    <span className="pv-split-rail-count">
                      {pool.readyCount}/{pool.targetCount}
                    </span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
          <button className="secondary pv-split-rail-new" onClick={c.startNewPool}>
            New pool
          </button>

          {c.settingsForm && (
            <div className="pv-split-schedule">
              <h3>Schedule</h3>
              <label>
                From (UTC)
                <input
                  type="time"
                  value={c.settingsForm.windowStartUtc ?? ""}
                  onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, windowStartUtc: e.target.value || null })}
                />
              </label>
              <label>
                Until (UTC)
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
              <p className="muted">Server clock {c.status?.nowUtc} UTC. Blank times allow any hour.</p>
            </div>
          )}
        </nav>

        <section className="pv-split-detail">
          {c.selected === null ? (
            <p className="muted pv-split-empty">Pick a pool on the left, or create one.</p>
          ) : (
            <>
              <div className="pv-split-detail-head">
                <h3>{c.selected === "new" ? "New pool" : c.poolForm.name}</h3>
                {selectedPool && (
                  <span className="muted pv-split-detail-reason">{selectedPool.statusReason}</span>
                )}
              </div>

              <div className="pv-split-fields">
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
                <label className="pv-split-check">
                  <input
                    type="checkbox"
                    checked={c.poolForm.enabled}
                    onChange={(e) => c.setPoolForm({ ...c.poolForm, enabled: e.target.checked })}
                  />
                  Generate in the background
                </label>
              </div>

              <label className="pv-split-prompt">
                Prompt
                <textarea
                  aria-label="Pool prompt"
                  rows={8}
                  value={c.poolForm.instructions}
                  onChange={(e) => c.setPoolForm({ ...c.poolForm, instructions: e.target.value })}
                />
              </label>

              <div className="pv-split-actions">
                <button disabled={c.busy || !c.poolForm.name.trim()} onClick={c.savePool}>
                  Save pool
                </button>
                {c.selected !== "new" && (
                  <>
                    <button className="secondary" disabled={c.busy} onClick={() => c.generateOne(c.selected as number)}>
                      Generate one now
                    </button>
                    {selectedPool && (
                      <button className="link-button" disabled={c.busy} onClick={() => c.deletePool(selectedPool)}>
                        Delete pool
                      </button>
                    )}
                  </>
                )}
              </div>

              {c.selected !== "new" && (
                <div className="pv-split-entries">
                  <h4>Ready entries ({c.entries.length})</h4>
                  {c.entries.length === 0 ? (
                    <p className="muted">Nothing generated yet.</p>
                  ) : (
                    c.entries.map((entry) => (
                      <div key={entry.id} className="pv-split-entry">
                        <span>{entry.content}</span>
                        <button className="link-button" disabled={c.busy} onClick={() => c.forget(entry.id)}>
                          Forget
                        </button>
                      </div>
                    ))
                  )}
                </div>
              )}
            </>
          )}
        </section>
      </div>
    </div>
  );
}
