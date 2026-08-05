import { useEffect, useState } from "react";
import { DocumentHistory, VersionSummary, api, extractErrorMessage } from "../api/client";
import { DiffViewer } from "./DiffViewer";

function formatVersionLabel(v: { createdAtUtc: string; username: string | null }): string {
  return `${new Date(v.createdAtUtc).toLocaleString()} · ${v.username ?? "Unknown"}`;
}

function groupByDay(versions: VersionSummary[]): { label: string; versions: VersionSummary[] }[] {
  const today = new Date().toDateString();
  const yesterday = new Date(Date.now() - 86_400_000).toDateString();
  const groups = new Map<string, VersionSummary[]>();
  for (const v of versions) {
    const key = new Date(v.createdAtUtc).toDateString();
    (groups.get(key) ?? groups.set(key, []).get(key)!).push(v);
  }
  return [...groups.entries()].map(([key, vs]) => ({
    label: key === today ? "Today" : key === yesterday ? "Yesterday" : new Date(vs[0].createdAtUtc).toLocaleDateString(),
    versions: vs,
  }));
}

interface DiffModalState {
  fromContent: string;
  fromLabel: string;
  toContent: string;
  toLabel: string;
  restoreVersionId?: number;
}

/** Discoverable "History" panel for a document - list, view/compare, and restore prior
 * versions. See Activity-And-History.md sections 1.6/1.9. */
export function VersionHistoryPanel({
  relativePath,
  onClose,
  onRestored,
}: {
  relativePath: string;
  onClose: () => void;
  onRestored: () => void;
}) {
  const [history, setHistory] = useState<DocumentHistory | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [selected, setSelected] = useState<number[]>([]);
  const [diffModal, setDiffModal] = useState<DiffModalState | null>(null);
  const [loadingVersionId, setLoadingVersionId] = useState<number | null>(null);
  const [restoreBusy, setRestoreBusy] = useState(false);

  const load = () => {
    api
      .getVersionHistory(relativePath)
      .then(setHistory)
      .catch((err) => setError(extractErrorMessage(err, "Couldn't load version history.")));
  };

  useEffect(load, [relativePath]);

  const toggleSelect = (id: number) => {
    setSelected((prev) => {
      if (prev.includes(id)) return prev.filter((x) => x !== id);
      if (prev.length >= 2) return prev;
      return [...prev, id];
    });
  };

  const viewVersion = async (index: number) => {
    if (!history) return;
    const to = history.versions[index];
    const from = history.versions[index + 1];
    setLoadingVersionId(to.id);
    setError(null);
    try {
      const toDetail = await api.getVersion(to.id);
      const fromContent = from ? (await api.getVersion(from.id)).content : "";
      const fromLabel = from ? formatVersionLabel(from) : "(page created)";
      setDiffModal({
        fromContent,
        fromLabel,
        toContent: toDetail.content,
        toLabel: formatVersionLabel(to),
        restoreVersionId: to.id,
      });
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't load that version."));
    } finally {
      setLoadingVersionId(null);
    }
  };

  const compareSelected = async () => {
    if (!history || selected.length !== 2) return;
    const indexOf = (id: number) => history.versions.findIndex((v) => v.id === id);
    // Versions list is newest-first, so the larger index is the older version.
    const [olderId, newerId] = indexOf(selected[0]) > indexOf(selected[1]) ? selected : [selected[1], selected[0]];
    setError(null);
    try {
      const result = await api.compareVersions(olderId, newerId);
      setDiffModal({
        fromContent: result.from.content,
        fromLabel: formatVersionLabel(result.from),
        toContent: result.to.content,
        toLabel: formatVersionLabel(result.to),
      });
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't compare those versions."));
    }
  };

  const restore = async (versionId: number) => {
    if (!window.confirm("Restore this version? Nothing is deleted - it becomes the new current version.")) return;
    setRestoreBusy(true);
    setError(null);
    try {
      await api.restoreVersion(versionId);
      setDiffModal(null);
      onRestored();
      load();
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't restore that version."));
    } finally {
      setRestoreBusy(false);
    }
  };

  return (
    <div className="modal-overlay" onClick={onClose}>
      <div className="modal modal-wide" onClick={(e) => e.stopPropagation()}>
        <h2>Version History</h2>
        {error && <div className="banner banner-error">{error}</div>}

        {!history ? (
          <p className="muted">Loading…</p>
        ) : history.versions.length === 0 ? (
          <p className="muted">No history yet - versions appear here after the first meaningful edit.</p>
        ) : (
          <>
            <div className="version-history-list">
              {groupByDay(history.versions).map((group) => (
                <div key={group.label} className="version-history-group">
                  <h3>{group.label}</h3>
                  {group.versions.map((v) => {
                    const index = history.versions.indexOf(v);
                    return (
                      <div key={v.id} className="version-history-row">
                        <input
                          type="checkbox"
                          checked={selected.includes(v.id)}
                          onChange={() => toggleSelect(v.id)}
                          disabled={!selected.includes(v.id) && selected.length >= 2}
                          title="Select to compare"
                        />
                        <span className="version-history-time">
                          {new Date(v.createdAtUtc).toLocaleTimeString([], { hour: "numeric", minute: "2-digit" })}
                        </span>
                        <span className="version-history-author">{v.username ?? "Unknown"}</span>
                        {v.versionType === "Restore" && <span className="admin-badge">Restored</span>}
                        {index === 0 && <span className="admin-badge admin-badge-ok">Current</span>}
                        <div className="version-history-row-actions">
                          <button className="secondary" disabled={loadingVersionId === v.id} onClick={() => void viewVersion(index)}>
                            Compare with Previous
                          </button>
                          <button className="secondary" disabled={restoreBusy || index === 0} onClick={() => void restore(v.id)}>
                            Restore
                          </button>
                        </div>
                      </div>
                    );
                  })}
                </div>
              ))}
            </div>
            <div className="modal-actions">
              <button className="secondary" disabled={selected.length !== 2} onClick={() => void compareSelected()}>
                Compare selected
              </button>
              <button className="secondary" onClick={onClose}>
                Close
              </button>
            </div>
          </>
        )}
      </div>

      {diffModal && (
        <div className="modal-overlay" onClick={(e) => { e.stopPropagation(); setDiffModal(null); }}>
          <div className="modal modal-wide" onClick={(e) => e.stopPropagation()}>
            <h2>Compare versions</h2>
            <DiffViewer
              oldContent={diffModal.fromContent}
              newContent={diffModal.toContent}
              oldLabel={diffModal.fromLabel}
              newLabel={diffModal.toLabel}
              headerAction={
                diffModal.restoreVersionId
                  ? { label: "Restore this version", onClick: () => void restore(diffModal.restoreVersionId!), busy: restoreBusy }
                  : undefined
              }
            />
            <div className="modal-actions">
              <button className="secondary" onClick={() => setDiffModal(null)}>
                Close
              </button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}
