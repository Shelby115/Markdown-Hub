import { GenerationPoolsController } from "./useGenerationPools";
import { NextCheckDial } from "./NextCheckDial";
import { generatorHeadline, percentFull, toneOf } from "./poolPresentation";

/**
 * Generation pools: a list rail of pools on the left, the selected one filling the pane on the
 * right. Every pool's status and fill level is readable from the rail without selecting it, so
 * "why isn't this one filling?" never needs a click to answer.
 */
export function GenerationPools({ c }: { c: GenerationPoolsController }) {
  const headline = generatorHeadline(c.status);
  const selectedPool = typeof c.selected === "number" ? c.pools?.find((p) => p.id === c.selected) : undefined;

  return (
    <div className="ai-pools">
      <div className="ai-pools-bar">
        <span className={`ai-pools-led ai-pools-led-${headline.tone}`} aria-hidden="true" />
        <strong>{headline.text}</strong>
        <span className="muted">{c.status?.reason}</span>
        {c.status && (
          <NextCheckDial
            seconds={c.status.secondsUntilNextCheck}
            intervalSeconds={c.status.settings.intervalSeconds}
            running={c.status.runningNow}
            working={c.status.generatingPoolName !== null}
          />
        )}
        <button className="secondary" disabled={c.busy} onClick={c.togglePause}>
          {c.status?.settings.paused ? "Resume" : "Pause"}
        </button>
      </div>

      {c.error && <div className="banner banner-error">{c.error}</div>}
      {c.notice && <div className="banner">{c.notice}</div>}

      <div className="ai-pools-body">
        <nav className="ai-pools-rail">
          <h3>Pools</h3>
          {c.pools?.length === 0 && <p className="muted ai-pools-rail-empty">None yet.</p>}
          <ul>
            {(c.pools ?? []).map((pool) => (
              <li key={pool.id}>
                <button
                  className={`ai-pools-rail-item${c.selected === pool.id ? " ai-pools-rail-item-active" : ""}`}
                  onClick={() => c.selectPool(pool)}
                >
                  <span className="ai-pools-rail-name">{pool.name}</span>
                  {/* The status belongs in the rail, not just the detail pane - otherwise you have
                      to select a pool to find out why it isn't filling. */}
                  <span className={`ai-pools-tag ai-pools-tag-${toneOf(pool.status)}`} title={pool.statusReason}>
                    {pool.status}
                  </span>
                  <span className="ai-pools-rail-meta">
                    <span className="ai-pools-rail-bar">
                      <span
                        className={pool.status === "Generating" ? "ai-pools-bar-busy" : undefined}
                        style={{ width: `${percentFull(pool)}%` }}
                      />
                    </span>
                    <span className="ai-pools-rail-count">
                      {pool.readyCount}/{pool.targetCount}
                    </span>
                  </span>
                </button>
              </li>
            ))}
          </ul>
          <button className="secondary ai-pools-rail-new" onClick={c.startNewPool}>
            New pool
          </button>

          {c.settingsForm && (
            <div className="ai-pools-schedule">
              <h3>Schedule</h3>
              <div className="ai-pools-schedule-pair">
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
              </div>
              <label>
                Seconds between checks
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
              <p className="muted">
                Server clock {c.status?.nowUtc} UTC. Leave both times blank to allow any hour; an end earlier than
                the start wraps past midnight.
              </p>
            </div>
          )}
        </nav>

        <section className="ai-pools-detail">
          {c.selected === null ? (
            <div className="ai-pools-empty muted">
              <p>Pick a pool on the left, or create one.</p>
              <p>
                A pool pre-generates content for one kind of template placeholder, so filling it is instant instead
                of waiting on the model. A template opts in by adding <code>- Pool: Name</code> to that placeholder
                in its <code>ai-template</code> block.
              </p>
            </div>
          ) : (
            <>
              <div className="ai-pools-detail-head">
                <h3>{c.selected === "new" ? "New pool" : c.poolForm.name}</h3>
                {selectedPool && <span className="muted">{selectedPool.statusReason}</span>}
              </div>

              <div className="ai-pools-fields">
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
                <label className="ai-pools-check">
                  <input
                    type="checkbox"
                    checked={c.poolForm.enabled}
                    onChange={(e) => c.setPoolForm({ ...c.poolForm, enabled: e.target.checked })}
                  />
                  Generate in the background
                </label>
              </div>

              <label className="ai-pools-prompt">
                <span>
                  Prompt - the same bullet rules a template's <code>ai-template</code> block uses, including{" "}
                  <code>Format:</code>, <code>Example:</code>, <code>Max words:</code>, and <code>Max sentences:</code>.
                </span>
                <textarea
                  aria-label="Pool prompt"
                  rows={8}
                  value={c.poolForm.instructions}
                  onChange={(e) => c.setPoolForm({ ...c.poolForm, instructions: e.target.value })}
                />
              </label>

              <div className="ai-pools-actions">
                <button disabled={c.busy || !c.poolForm.name.trim()} onClick={c.savePool}>
                  Save pool
                </button>
                {c.selected !== "new" && (
                  <>
                    <button className="secondary" disabled={c.busy} onClick={() => c.generateOne(c.selected as number)}>
                      Generate one now
                    </button>
                    {selectedPool && (
                      <button className="link-button ai-pools-delete" disabled={c.busy} onClick={() => c.deletePool(selectedPool)}>
                        Delete pool
                      </button>
                    )}
                  </>
                )}
              </div>

              {c.selected !== "new" && (
                <div className="ai-pools-entries">
                  <h4>Ready entries ({c.entries.length})</h4>
                  {c.entries.length === 0 ? (
                    <p className="muted">Nothing generated yet.</p>
                  ) : (
                    c.entries.map((entry) => (
                      <div key={entry.id} className="ai-pools-entry">
                        <span>{entry.content}</span>
                        <button
                          className="link-button"
                          disabled={c.busy}
                          title="Forget this entry - never show or regenerate it"
                          onClick={() => c.forget(entry.id)}
                        >
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
