import { GenerationPool } from "../../api/client";
import { GenerationPoolsController } from "./useGenerationPools";
import { generatorHeadline, percentFull, toneOf } from "./poolPresentation";

/**
 * Variant D - "shelf". Leads with a large live status banner, then each pool is a full-width row
 * whose detail expands in place. Nothing is hidden behind a modal or a second pane: what you see
 * is the whole page, top to bottom.
 */
export function PoolsShelf({ c }: { c: GenerationPoolsController }) {
  const headline = generatorHeadline(c.status);

  return (
    <div className="pv pv-shelf">
      <div className={`pv-shelf-banner pv-shelf-banner-${headline.tone}`}>
        <div className="pv-shelf-banner-main">
          <span className={`pv-pulse pv-pulse-${headline.tone}`} aria-hidden="true" />
          <div>
            <h3>{headline.text}</h3>
            <p>{c.status?.reason}</p>
          </div>
        </div>
        <div className="pv-shelf-banner-side">
          <span className="pv-shelf-clock">{c.status?.nowUtc} UTC</span>
          <button className="secondary" disabled={c.busy} onClick={c.togglePause}>
            {c.status?.settings.paused ? "Resume generating" : "Pause generating"}
          </button>
        </div>
      </div>

      {c.settingsForm && (
        <div className="pv-shelf-schedule">
          <label>
            Allowed from
            <input
              type="time"
              value={c.settingsForm.windowStartUtc ?? ""}
              onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, windowStartUtc: e.target.value || null })}
            />
          </label>
          <label>
            Allowed until
            <input
              type="time"
              value={c.settingsForm.windowEndUtc ?? ""}
              onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, windowEndUtc: e.target.value || null })}
            />
          </label>
          <label>
            Every
            <input
              type="number"
              min={10}
              value={c.settingsForm.intervalSeconds}
              onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, intervalSeconds: Number(e.target.value) })}
            />
            <em>seconds</em>
          </label>
          <label>
            Keep used
            <input
              type="number"
              min={0}
              value={c.settingsForm.usedEntryRetentionDays}
              onChange={(e) => c.setSettingsForm({ ...c.settingsForm!, usedEntryRetentionDays: Number(e.target.value) })}
            />
            <em>days</em>
          </label>
          <button disabled={c.busy} onClick={() => c.saveSettings(c.settingsForm!)}>
            Save schedule
          </button>
        </div>
      )}

      {c.error && <div className="banner banner-error">{c.error}</div>}
      {c.notice && <div className="banner">{c.notice}</div>}

      <div className="pv-shelf-list">
        {(c.pools ?? []).map((pool) => (
          <PoolRow key={pool.id} pool={pool} c={c} />
        ))}
        {c.pools?.length === 0 && <p className="muted">No pools yet.</p>}
      </div>

      {c.selected === "new" ? (
        <div className="pv-shelf-row pv-shelf-row-open">
          <div className="pv-shelf-detail">
            <label className="pv-shelf-field">
              Name
              <input
                value={c.poolForm.name}
                placeholder="Interactible"
                onChange={(e) => c.setPoolForm({ ...c.poolForm, name: e.target.value })}
              />
            </label>
            <PoolEditorBody c={c} />
          </div>
        </div>
      ) : (
        <button className="secondary pv-shelf-add" onClick={c.startNewPool}>
          New pool
        </button>
      )}
    </div>
  );
}

function PoolRow({ pool, c }: { pool: GenerationPool; c: GenerationPoolsController }) {
  const open = c.selected === pool.id;
  const tone = toneOf(pool.status);

  return (
    <div className={`pv-shelf-row${open ? " pv-shelf-row-open" : ""}`}>
      <button
        className="pv-shelf-row-head"
        onClick={() => (open ? c.closeEditor() : c.selectPool(pool))}
        aria-expanded={open}
      >
        <span className="pv-shelf-chevron" aria-hidden="true">
          {open ? "▾" : "▸"}
        </span>
        <span className="pv-shelf-name">{pool.name}</span>
        <span className="pv-shelf-bar">
          <span
            className={`pv-shelf-bar-fill${pool.status === "Generating" ? " pv-shelf-bar-busy" : ""}`}
            style={{ width: `${percentFull(pool)}%` }}
          />
        </span>
        <span className="pv-shelf-count">
          {pool.readyCount}
          <em>/{pool.targetCount}</em>
        </span>
        <span className={`pv-tag pv-tag-${tone}`} title={pool.statusReason}>
          {pool.status}
        </span>
      </button>

      {open && (
        <div className="pv-shelf-detail">
          <p className="muted pv-shelf-reason">{pool.statusReason}</p>
          <PoolEditorBody c={c} />
        </div>
      )}
    </div>
  );
}

/** The fields shared by the expanded row and the new-pool row. */
function PoolEditorBody({ c }: { c: GenerationPoolsController }) {
  const open = typeof c.selected === "number" ? c.pools?.find((p) => p.id === c.selected) : undefined;

  return (
    <>
      <div className="pv-shelf-fields">
        <label className="pv-shelf-field">
          Entries to keep ready
          <input
            type="number"
            min={0}
            value={c.poolForm.targetCount}
            onChange={(e) => c.setPoolForm({ ...c.poolForm, targetCount: Number(e.target.value) })}
          />
        </label>
        <label className="pv-shelf-check">
          <input
            type="checkbox"
            checked={c.poolForm.enabled}
            onChange={(e) => c.setPoolForm({ ...c.poolForm, enabled: e.target.checked })}
          />
          Generate in the background
        </label>
      </div>

      <label className="pv-shelf-prompt">
        Prompt
        <textarea
          aria-label="Pool prompt"
          rows={7}
          value={c.poolForm.instructions}
          onChange={(e) => c.setPoolForm({ ...c.poolForm, instructions: e.target.value })}
        />
      </label>

      <div className="pv-shelf-actions">
        <button disabled={c.busy || !c.poolForm.name.trim()} onClick={c.savePool}>
          Save pool
        </button>
        {typeof c.selected === "number" && (
          <button className="secondary" disabled={c.busy} onClick={() => c.generateOne(c.selected as number)}>
            Generate one now
          </button>
        )}
        <button className="link-button" onClick={c.closeEditor}>
          Cancel
        </button>
        {open && (
          <button className="link-button pv-shelf-delete" disabled={c.busy} onClick={() => c.deletePool(open)}>
            Delete pool
          </button>
        )}
      </div>

      {typeof c.selected === "number" && (
        <div className="pv-shelf-entries">
          <h4>Ready entries ({c.entries.length})</h4>
          {c.entries.length === 0 ? (
            <p className="muted">Nothing generated yet.</p>
          ) : (
            <div className="pv-shelf-entry-grid">
              {c.entries.map((entry) => (
                <div key={entry.id} className="pv-shelf-entry">
                  <span>{entry.content}</span>
                  <button className="link-button" disabled={c.busy} onClick={() => c.forget(entry.id)}>
                    Forget
                  </button>
                </div>
              ))}
            </div>
          )}
        </div>
      )}
    </>
  );
}
