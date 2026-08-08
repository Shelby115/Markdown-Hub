import { GenerationPoolsController } from "./useGenerationPools";
import { generatorHeadline, toneOf } from "./poolPresentation";

/**
 * Variant A - "control room". Dense and utilitarian: one status rail across the top, every pool
 * on one line with a segmented meter, and the editor as a drawer underneath. Optimised for seeing
 * the state of everything at once without scrolling.
 */
export function PoolsConsole({ c }: { c: GenerationPoolsController }) {
  const headline = generatorHeadline(c.status);

  return (
    <div className="pv pv-console">
      <div className="pv-console-rail">
        <span className={`pv-led pv-led-${headline.tone}`} aria-hidden="true" />
        <span className="pv-console-headline">{headline.text}</span>
        <span className="pv-console-reason">{c.status?.reason}</span>
        <span className="pv-console-clock">{c.status?.nowUtc} UTC</span>
        <button className="secondary" disabled={c.busy} onClick={c.togglePause}>
          {c.status?.settings.paused ? "Resume" : "Pause"}
        </button>
      </div>

      {c.settingsForm && (
        <div className="pv-console-strip">
          <label>
            <span>WINDOW</span>
            <div className="pv-console-range">
              <input
                type="time"
                value={c.settingsForm.windowStartUtc ?? ""}
                onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, windowStartUtc: e.target.value || null })}
              />
              <em>→</em>
              <input
                type="time"
                value={c.settingsForm.windowEndUtc ?? ""}
                onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, windowEndUtc: e.target.value || null })}
              />
            </div>
          </label>
          <label>
            <span>INTERVAL</span>
            <input
              type="number"
              min={10}
              value={c.settingsForm.intervalSeconds}
              onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, intervalSeconds: Number(e.target.value) })}
            />
          </label>
          <label>
            <span>KEEP USED</span>
            <input
              type="number"
              min={0}
              value={c.settingsForm.usedEntryRetentionDays}
              onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, usedEntryRetentionDays: Number(e.target.value) })}
            />
          </label>
          <button disabled={c.busy} onClick={() => c.saveSettings(c.settingsForm!)}>
            Apply
          </button>
        </div>
      )}

      {c.error && <div className="banner banner-error">{c.error}</div>}
      {c.notice && <div className="banner">{c.notice}</div>}

      <div className="pv-console-pools">
        {(c.pools ?? []).map((pool) => (
          <div
            key={pool.id}
            className={`pv-console-pool${c.selected === pool.id ? " pv-console-pool-open" : ""}`}
            onClick={() => c.selectPool(pool)}
          >
            <span className="pv-console-pool-name">{pool.name}</span>
            <Meter filled={pool.readyCount} total={pool.targetCount} busy={pool.status === "Generating"} />
            <span className="pv-console-pool-count">
              {String(pool.readyCount).padStart(2, "0")}/{String(pool.targetCount).padStart(2, "0")}
            </span>
            <span className={`pv-tag pv-tag-${toneOf(pool.status)}`} title={pool.statusReason}>
              {pool.status}
            </span>
            <button
              className="link-button"
              onClick={(e) => {
                e.stopPropagation();
                c.deletePool(pool);
              }}
            >
              del
            </button>
          </div>
        ))}
        {c.pools?.length === 0 && <p className="muted">No pools yet.</p>}
        <button className="link-button pv-console-add" onClick={c.startNewPool}>
          + new pool
        </button>
      </div>

      {c.selected !== null && (
        <div className="pv-console-drawer">
          <div className="pv-console-drawer-head">
            <span>{c.selected === "new" ? "NEW POOL" : `EDIT — ${c.poolForm.name.toUpperCase()}`}</span>
            <button className="link-button" onClick={c.closeEditor}>
              close
            </button>
          </div>
          <div className="pv-console-drawer-body">
            <div className="pv-console-drawer-form">
              {c.selected === "new" && (
                <label>
                  <span>NAME</span>
                  <input value={c.poolForm.name} onChange={(e) => c.setPoolForm({ ...c.poolForm, name: e.target.value })} />
                </label>
              )}
              <label>
                <span>TARGET</span>
                <input
                  type="number"
                  min={0}
                  value={c.poolForm.targetCount}
                  onChange={(e) => c.setPoolForm({ ...c.poolForm, targetCount: Number(e.target.value) })}
                />
              </label>
              <label className="pv-console-check">
                <input
                  type="checkbox"
                  checked={c.poolForm.enabled}
                  onChange={(e) => c.setPoolForm({ ...c.poolForm, enabled: e.target.checked })}
                />
                <span>AUTO-FILL</span>
              </label>
              <textarea
                aria-label="Pool prompt"
                rows={7}
                value={c.poolForm.instructions}
                onChange={(e) => c.setPoolForm({ ...c.poolForm, instructions: e.target.value })}
              />
              <div className="pv-console-drawer-actions">
                <button disabled={c.busy || !c.poolForm.name.trim()} onClick={c.savePool}>
                  Save
                </button>
                {c.selected !== "new" && (
                  <button className="secondary" disabled={c.busy} onClick={() => c.generateOne(c.selected as number)}>
                    Generate one
                  </button>
                )}
              </div>
            </div>

            {c.selected !== "new" && (
              <ol className="pv-console-entries">
                {c.entries.map((entry, i) => (
                  <li key={entry.id}>
                    <span className="pv-console-entry-index">{String(i + 1).padStart(2, "0")}</span>
                    <span>{entry.content}</span>
                    <button className="link-button" disabled={c.busy} onClick={() => c.forget(entry.id)}>
                      forget
                    </button>
                  </li>
                ))}
                {c.entries.length === 0 && <p className="muted">Empty.</p>}
              </ol>
            )}
          </div>
        </div>
      )}
    </div>
  );
}

/** A segmented bar - reads as a fuel gauge rather than a percentage. */
function Meter({ filled, total, busy }: { filled: number; total: number; busy: boolean }) {
  const segments = 16;
  const lit = total === 0 ? 0 : Math.round((Math.min(filled, total) / total) * segments);
  return (
    <span className={`pv-meter${busy ? " pv-meter-busy" : ""}`} aria-hidden="true">
      {Array.from({ length: segments }, (_, i) => (
        <i key={i} className={i < lit ? "pv-meter-on" : undefined} />
      ))}
    </span>
  );
}
