import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { ActivityDetail, ActivitySummary, AdminUser, api, extractErrorMessage } from "../api/client";
import { DiffViewer } from "../components/DiffViewer";

const PAGE_SIZE = 50;

function describeActivity(item: ActivitySummary): string {
  const who = item.username ?? (item.ipAddress ? item.ipAddress : "Someone");
  const target = item.targetPath;
  const suffix = item.occurrenceCount > 1 ? ` (×${item.occurrenceCount})` : "";

  switch (item.action) {
    case "Auth.Login":
      return `${who} logged in${suffix}`;
    case "Auth.Logout":
      return `${who} logged out${suffix}`;
    case "Auth.TokenRejected":
      return `Rejected API request from ${who}${suffix}`;
    case "File.Create":
      return `${who} created "${target}"${suffix}`;
    case "File.Modify":
      return `${who} modified "${target}"${suffix}`;
    case "File.Delete":
      return `${who} deleted "${target}"${suffix}`;
    case "File.Restore":
      return `${who} restored "${target}"${suffix}`;
    case "File.Rename":
      return `${who} renamed ${target}${suffix}`;
    case "File.Move":
      return `${who} moved ${target}${suffix}`;
    case "Folder.Create":
      return `${who} created folder "${target}"${suffix}`;
    case "User.Create":
      return `${who} created user "${target}"${suffix}`;
    case "User.Delete":
      return `${who} deleted user "${target}"${suffix}`;
    case "User.Promote":
      return `${who} promoted "${target}" to administrator${suffix}`;
    case "User.Demote":
      return `${who} removed administrator from "${target}"${suffix}`;
    case "User.Disable":
      return `${who} disabled user "${target}"${suffix}`;
    case "User.Enable":
      return `${who} enabled user "${target}"${suffix}`;
    case "Permission.Grant":
      return `${who} changed permissions for "${target}"${suffix}`;
    case "Permission.Revoke":
      return `${who} revoked permissions for "${target}"${suffix}`;
    case "AiSettings.SetModel":
      return `${who} changed the AI model${suffix}`;
    case "Settings.HistoryRetention":
      return `${who} changed retention settings${suffix}`;
    default:
      return `${who} — ${item.action}${target ? ` (${target})` : ""}${suffix}`;
  }
}

const canShowDiff = (item: ActivitySummary) =>
  item.objectType === "Document" &&
  item.relatedVersionId != null &&
  ["File.Create", "File.Modify", "File.Restore"].includes(item.action) &&
  !!item.targetPath;

interface DiffModalState {
  fromContent: string;
  fromLabel: string;
  toContent: string;
  toLabel: string;
}

export function ActivityLogPage() {
  const [items, setItems] = useState<ActivitySummary[] | null>(null);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);

  const [users, setUsers] = useState<AdminUser[]>([]);
  const [actionTypes, setActionTypes] = useState<string[]>([]);

  const [fromFilter, setFromFilter] = useState("");
  const [toFilter, setToFilter] = useState("");
  const [userFilter, setUserFilter] = useState("");
  const [actionFilter, setActionFilter] = useState("");
  const [objectSearch, setObjectSearch] = useState("");

  const [expandedId, setExpandedId] = useState<number | null>(null);
  const [detail, setDetail] = useState<ActivityDetail | null>(null);
  const [detailBusy, setDetailBusy] = useState(false);
  const [diffModal, setDiffModal] = useState<DiffModalState | null>(null);
  const [diffBusy, setDiffBusy] = useState(false);

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  const load = async (targetPage: number) => {
    setLoading(true);
    setError(null);
    try {
      const result = await api.adminQueryActivity({
        from: fromFilter ? new Date(fromFilter).toISOString() : undefined,
        to: toFilter ? new Date(toFilter).toISOString() : undefined,
        userId: userFilter ? Number(userFilter) : undefined,
        action: actionFilter || undefined,
        objectSearch: objectSearch.trim() || undefined,
        page: targetPage,
        pageSize: PAGE_SIZE,
      });
      setItems(result.items);
      setTotalCount(result.totalCount);
      setPage(result.page);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't load activity."));
    } finally {
      setLoading(false);
    }
  };

  useEffect(() => {
    void load(1);
    api.adminListUsers().then(setUsers).catch(() => {});
    api.adminGetActivityActionTypes().then(setActionTypes).catch(() => {});
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const submitFilters = (e: React.FormEvent) => {
    e.preventDefault();
    void load(1);
  };

  const toggleExpand = async (item: ActivitySummary) => {
    if (expandedId === item.id) {
      setExpandedId(null);
      setDetail(null);
      return;
    }
    setExpandedId(item.id);
    setDetail(null);
    setDetailBusy(true);
    try {
      setDetail(await api.adminGetActivityDetail(item.id));
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't load that event's details."));
    } finally {
      setDetailBusy(false);
    }
  };

  const viewDiff = async (item: ActivitySummary) => {
    if (!item.relatedVersionId || !item.targetPath) return;
    setDiffBusy(true);
    setError(null);
    try {
      const history = await api.getVersionHistory(item.targetPath);
      const index = history.versions.findIndex((v) => v.id === item.relatedVersionId);
      if (index === -1) {
        setError("That version is no longer available (it may have been cleaned up by retention).");
        return;
      }
      const to = await api.getVersion(history.versions[index].id);
      const from = history.versions[index + 1];
      const fromContent = from ? (await api.getVersion(from.id)).content : "";
      const fromLabel = from ? `${new Date(from.createdAtUtc).toLocaleString()} · ${from.username ?? "Unknown"}` : "(page created)";
      setDiffModal({
        fromContent,
        fromLabel,
        toContent: to.content,
        toLabel: `${new Date(to.createdAtUtc).toLocaleString()} · ${to.username ?? "Unknown"}`,
      });
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't load the before/after diff."));
    } finally {
      setDiffBusy(false);
    }
  };

  return (
    <div className="admin-page activity-log-page">
      <div className="admin-page-header">
        <h1>Activity Log</h1>
        <Link className="link-button" to="/admin">
          ← Admin
        </Link>
      </div>
      {error && <div className="banner banner-error">{error}</div>}

      <form className="activity-filters" onSubmit={submitFilters}>
        <label>
          From
          <input type="date" value={fromFilter} onChange={(e) => setFromFilter(e.target.value)} />
        </label>
        <label>
          To
          <input type="date" value={toFilter} onChange={(e) => setToFilter(e.target.value)} />
        </label>
        <label>
          User
          <select value={userFilter} onChange={(e) => setUserFilter(e.target.value)}>
            <option value="">Anyone</option>
            {users.map((u) => (
              <option key={u.id} value={u.id}>
                {u.username}
              </option>
            ))}
          </select>
        </label>
        <label>
          Action
          <select value={actionFilter} onChange={(e) => setActionFilter(e.target.value)}>
            <option value="">Any</option>
            {actionTypes.map((a) => (
              <option key={a} value={a}>
                {a}
              </option>
            ))}
          </select>
        </label>
        <label>
          Search object/path
          <input
            type="text"
            placeholder="e.g. Campaign/Session 5.md"
            value={objectSearch}
            onChange={(e) => setObjectSearch(e.target.value)}
          />
        </label>
        <button type="submit" disabled={loading}>
          Apply filters
        </button>
      </form>

      {!items ? (
        <p className="muted">Loading…</p>
      ) : items.length === 0 ? (
        <p className="muted">No activity in this range.</p>
      ) : (
        <>
          <div className="activity-list">
            {items.map((item) => (
              <div key={item.id} className="activity-row">
                <div className="activity-row-summary" onClick={() => void toggleExpand(item)}>
                  <span className="activity-row-time">{new Date(item.timestamp).toLocaleString()}</span>
                  <span className="activity-row-text">{describeActivity(item)}</span>
                  <span className="chevron activity-row-chevron">{expandedId === item.id ? "▾" : "▸"}</span>
                </div>
                {expandedId === item.id && (
                  <div className="activity-row-details">
                    {detailBusy || !detail ? (
                      <p className="muted">Loading…</p>
                    ) : (
                      <>
                        <dl className="activity-detail-fields">
                          <dt>User</dt>
                          <dd>{detail.username ?? "(unauthenticated)"}</dd>
                          <dt>Action</dt>
                          <dd>{detail.action}</dd>
                          {detail.targetPath && (
                            <>
                              <dt>Object</dt>
                              <dd>{detail.targetPath}</dd>
                            </>
                          )}
                          <dt>Time</dt>
                          <dd>
                            {detail.occurrenceCount > 1 && detail.lastOccurredAtUtc
                              ? `${new Date(detail.timestamp).toLocaleString()} – ${new Date(detail.lastOccurredAtUtc).toLocaleString()} (${detail.occurrenceCount} occurrences)`
                              : new Date(detail.timestamp).toLocaleString()}
                          </dd>
                          <dt>IP address</dt>
                          <dd>{detail.ipAddress ?? "—"}</dd>
                          {detail.details && (
                            <>
                              <dt>Details</dt>
                              <dd>{detail.details}</dd>
                            </>
                          )}
                        </dl>
                        {canShowDiff(item) && (
                          <button className="secondary" disabled={diffBusy} onClick={() => void viewDiff(item)}>
                            View Before/After
                          </button>
                        )}
                      </>
                    )}
                  </div>
                )}
              </div>
            ))}
          </div>

          <div className="activity-pagination">
            <button className="secondary" disabled={page <= 1 || loading} onClick={() => void load(page - 1)}>
              ← Previous
            </button>
            <span className="muted">
              Page {page} of {totalPages} ({totalCount} total)
            </span>
            <button className="secondary" disabled={page >= totalPages || loading} onClick={() => void load(page + 1)}>
              Next →
            </button>
          </div>
        </>
      )}

      {diffModal && (
        <div className="modal-overlay" onClick={() => setDiffModal(null)}>
          <div className="modal modal-wide" onClick={(e) => e.stopPropagation()}>
            <h2>Before / After</h2>
            <DiffViewer
              oldContent={diffModal.fromContent}
              newContent={diffModal.toContent}
              oldLabel={diffModal.fromLabel}
              newLabel={diffModal.toLabel}
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
