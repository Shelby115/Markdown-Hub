import { useCallback, useEffect, useState } from "react";
import { useParams } from "react-router-dom";
import { DeletedDocument, PageContent, api, extractErrorMessage } from "../api/client";
import { Editor } from "../components/Editor";
import { Backlinks } from "../components/Backlinks";

export function PageView() {
  const params = useParams();
  // react-router's "*" splat param carries the full remaining path, e.g. "Projects/Ideas".
  const relativePath = (params["*"] ?? "") + ".md";

  const [page, setPage] = useState<PageContent | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [deletedInfo, setDeletedInfo] = useState<DeletedDocument | null>(null);
  const [restoreBusy, setRestoreBusy] = useState(false);

  useEffect(() => {
    setPage(null);
    setError(null);
    setDeletedInfo(null);
    // Always loaded fresh from the server (never cached client-side) so external
    // edits made outside the app are picked up whenever a page is opened.
    api
      .getPage(relativePath)
      .then(setPage)
      .catch(async () => {
        // The page may simply not exist, or the user may lack access - but it could also be a
        // soft-deleted document they're authorized (Manage permission) to recover. Only users
        // who can see it in the deleted-documents list learn it was ever deleted at all, so this
        // never leaks more than their existing permissions already allow.
        try {
          const deleted = await api.listDeletedDocuments();
          const match = deleted.find((d) => d.relativePath === relativePath);
          if (match) {
            setDeletedInfo(match);
            return;
          }
        } catch {
          /* not authorized to see deleted documents at all - fall through to the generic error */
        }
        setError("This page couldn't be loaded, or you don't have access to it.");
      });
  }, [relativePath]);

  const restoreDeleted = async () => {
    if (!deletedInfo?.latestVersionId) return;
    setRestoreBusy(true);
    setError(null);
    try {
      await api.restoreVersion(deletedInfo.latestVersionId);
      setDeletedInfo(null);
      const fresh = await api.getPage(relativePath);
      setPage(fresh);
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't restore this page."));
    } finally {
      setRestoreBusy(false);
    }
  };

  // Editor.tsx flushes a pending autosave on unmount (e.g. when navigating away mid-debounce).
  // That save resolves asynchronously, potentially after the user has already navigated to and
  // loaded a *different* page - so this only ever applies the result if it still matches
  // whatever page is currently showing, rather than trusting the stale closure.
  const handleSaved = useCallback((saved: PageContent) => {
    setPage((current) => (current && current.relativePath === saved.relativePath ? saved : current));
  }, []);

  if (error) return <div className="banner banner-error">{error}</div>;
  if (deletedInfo) {
    return (
      <div className="banner banner-warning">
        "{deletedInfo.pageName}" was deleted
        {deletedInfo.deletedAtUtc && ` on ${new Date(deletedInfo.deletedAtUtc).toLocaleString()}`}
        {deletedInfo.deletedByUsername && ` by ${deletedInfo.deletedByUsername}`}. Nothing else is lost - restoring
        brings back its last known content as a new current version.
        <div style={{ marginTop: "0.5rem" }}>
          <button disabled={restoreBusy || !deletedInfo.latestVersionId} onClick={() => void restoreDeleted()}>
            Restore this page
          </button>
        </div>
      </div>
    );
  }
  if (!page) return <div className="muted">Loading…</div>;

  return (
    <div className="page-view">
      <Editor key={page.relativePath} page={page} onSaved={handleSaved} />
      <Backlinks relativePath={page.relativePath} />
    </div>
  );
}
