import { redo, redoDepth, undo, undoDepth } from "@codemirror/commands";
import { markdown } from "@codemirror/lang-markdown";
import { EditorView, type ViewUpdate } from "@codemirror/view";
import { Table } from "@lezer/markdown";
import CodeMirror, { type ReactCodeMirrorRef } from "@uiw/react-codemirror";
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { useNavigate } from "react-router-dom";
import { api, PageContent } from "../api/client";
import { toPageUrl } from "../pageUrl";
import { liveMarkdownPreview } from "./liveMarkdown";
import { VersionHistoryPanel } from "./VersionHistoryPanel";

type SaveState = "idle" | "saving" | "saved" | "conflict" | "error";
const AUTOSAVE_DELAY_MS = 2000;

export function Editor({ page, onSaved }: { page: PageContent; onSaved: (p: PageContent) => void }) {
  const navigate = useNavigate();
  const [draft, setDraft] = useState(page.content);
  const [saveState, setSaveState] = useState<SaveState>("idle");
  const [conflictPath, setConflictPath] = useState<string | null>(null);
  const [isPublished, setIsPublished] = useState(page.isPublished);
  const [publishSlug, setPublishSlug] = useState(page.publishSlug);
  const [publishBusy, setPublishBusy] = useState(false);
  const [isTemplate, setIsTemplate] = useState(page.isTemplate);
  const [templateBusy, setTemplateBusy] = useState(false);
  const [canUndo, setCanUndo] = useState(false);
  const [canRedo, setCanRedo] = useState(false);
  const [historyOpen, setHistoryOpen] = useState(false);
  const editorRef = useRef<ReactCodeMirrorRef>(null);

  // Refs (rather than state) for values the debounce/flush logic needs without
  // re-running effects on every keystroke.
  const draftRef = useRef(draft);
  const lastSavedContentRef = useRef(page.content);
  const lastLoadedMtimeRef = useRef(page.lastModifiedUtc);
  const pausedForConflictRef = useRef(false);
  const timerRef = useRef<number>();

  const doSave = useCallback(async () => {
    if (pausedForConflictRef.current) return;
    const content = draftRef.current;
    if (content === lastSavedContentRef.current) return;

    setSaveState("saving");
    try {
      const saved = await api.savePage(page.relativePath, content, lastLoadedMtimeRef.current);
      lastLoadedMtimeRef.current = saved.lastModifiedUtc;
      lastSavedContentRef.current = content;
      setSaveState("saved");
      onSaved(saved);
    } catch (err: any) {
      if (err.status === 409) {
        pausedForConflictRef.current = true;
        setSaveState("conflict");
        try {
          const parsed = JSON.parse(err.message);
          setConflictPath(parsed.conflictRelativePath);
        } catch {
          /* ignore parse failure, still show generic conflict state */
        }
      } else {
        setSaveState("error");
      }
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [page.relativePath]);

  const scheduleSave = useCallback(() => {
    if (timerRef.current) window.clearTimeout(timerRef.current);
    timerRef.current = window.setTimeout(() => void doSave(), AUTOSAVE_DELAY_MS);
  }, [doSave]);

  // Flush any pending edit immediately if the user navigates away mid-debounce.
  useEffect(() => {
    return () => {
      if (timerRef.current) {
        window.clearTimeout(timerRef.current);
        void doSave();
      }
    };
  }, [doSave]);

  const handleChange = (value: string) => {
    draftRef.current = value;
    setDraft(value);
    scheduleSave();
  };

  // CodeMirror's own history extension (part of basicSetup) already tracks undo/redo state and
  // already handles Ctrl+Z / Ctrl+Shift+Z via its default keymap - this just mirrors that state
  // into React so the toolbar buttons below can reflect and trigger it too.
  const handleUpdate = useCallback((viewUpdate: ViewUpdate) => {
    setCanUndo(undoDepth(viewUpdate.state) > 0);
    setCanRedo(redoDepth(viewUpdate.state) > 0);
  }, []);

  const handleUndo = () => {
    const view = editorRef.current?.view;
    if (view) undo(view);
  };

  const handleRedo = () => {
    const view = editorRef.current?.view;
    if (view) redo(view);
  };

  const reloadLatest = async () => {
    const fresh = await api.getPage(page.relativePath);
    draftRef.current = fresh.content;
    lastSavedContentRef.current = fresh.content;
    lastLoadedMtimeRef.current = fresh.lastModifiedUtc;
    pausedForConflictRef.current = false;
    setDraft(fresh.content);
    setSaveState("idle");
    setConflictPath(null);
    onSaved(fresh);
  };

  const togglePublish = async () => {
    setPublishBusy(true);
    try {
      const result = await api.setPublished(page.relativePath, !isPublished);
      setIsPublished(result.isPublished);
      setPublishSlug(result.publishSlug);
      onSaved({ ...page, isPublished: result.isPublished, publishSlug: result.publishSlug });
    } finally {
      setPublishBusy(false);
    }
  };

  const toggleTemplate = async () => {
    setTemplateBusy(true);
    try {
      const result = await api.setTemplate(page.relativePath, !isTemplate);
      setIsTemplate(result.isTemplate);
      onSaved({ ...page, isTemplate: result.isTemplate });
    } finally {
      setTemplateBusy(false);
    }
  };

  const onImagePaste = useCallback(
    async (file: File, view: EditorView) => {
      const folder = page.relativePath.split("/").slice(0, -1).join("/");
      const { markdownSyntax } = await api.uploadAttachment(folder, file);
      const { from, to } = view.state.selection.main;
      view.dispatch({ changes: { from, to, insert: markdownSyntax } });
    },
    [page.relativePath]
  );

  // Folder a newly-created page (e.g. from clicking a red/missing link) should land in -
  // alongside whatever page is currently open, not the hub root.
  const currentFolder = page.relativePath.split("/").slice(0, -1).join("/");

  const extensions = useMemo(
    () => [
      markdown({ extensions: [Table] }),
      liveMarkdownPreview((relativePath) => navigate(toPageUrl(relativePath)), currentFolder),
      EditorView.lineWrapping,
      EditorView.domEventHandlers({
        paste(event, view) {
          const item = Array.from(event.clipboardData?.items ?? []).find((i) => i.type.startsWith("image/"));
          if (!item) return false;
          event.preventDefault();
          const file = item.getAsFile();
          if (file) void onImagePaste(file, view);
          return true;
        },
      }),
    ],
    [onImagePaste, navigate, currentFolder]
  );

  return (
    <div className="editor">
      <div className="editor-toolbar">
        <h1>{page.pageName}</h1>
        <div className="editor-toolbar-right-group">
          <div className="editor-toolbar-right">
            <div className="undo-redo-controls">
              <button className="icon-button" title="Undo (Ctrl+Z)" disabled={!canUndo} onClick={handleUndo}>
                ↶
              </button>
              <button className="icon-button" title="Redo (Ctrl+Shift+Z)" disabled={!canRedo} onClick={handleRedo}>
                ↷
              </button>
            </div>
            <button className="secondary" onClick={() => setHistoryOpen(true)}>
              History
            </button>
            <button
              className={`secondary template-toggle-button${isPublished ? " template-toggle-active" : ""}`}
              disabled={publishBusy}
              onClick={togglePublish}
              aria-pressed={isPublished}
            >
              {isPublished ? "☑" : "☐"} Published
            </button>
            <button
              className={`secondary template-toggle-button${isTemplate ? " template-toggle-active" : ""}`}
              disabled={templateBusy}
              onClick={toggleTemplate}
              aria-pressed={isTemplate}
            >
              {isTemplate ? "☑" : "☐"} Template
            </button>
            <div className="save-status">
              {saveState === "saving" && "Saving…"}
              {saveState === "saved" && "Saved"}
              {saveState === "error" && <span className="save-status-error">Couldn't save</span>}
            </div>
          </div>
          <div className="editor-toolbar-secondary-row">
            <span className="publish-link" style={{ visibility: isPublished && publishSlug ? "visible" : "hidden" }}>
              <a href={publishSlug ? `/published/${publishSlug}` : "#"} target="_blank" rel="noopener noreferrer">
                {window.location.origin}/published/{publishSlug ?? "…"}
              </a>
            </span>
          </div>
        </div>
      </div>

      {saveState === "conflict" && (
        <div className="banner banner-warning">
          This file changed elsewhere while you were editing. Your version was saved separately
          {conflictPath ? <> as <code>{conflictPath}</code></> : null}. Autosave is paused - reload to keep
          editing the latest version (your conflict copy is safe on disk either way).
          <div style={{ marginTop: "0.5rem" }}>
            <button onClick={reloadLatest}>Reload latest version</button>
          </div>
        </div>
      )}

      <CodeMirror
        ref={editorRef}
        value={draft}
        onChange={handleChange}
        onUpdate={handleUpdate}
        extensions={extensions}
        theme="none"
        basicSetup={{
          lineNumbers: false,
          foldGutter: false,
          highlightActiveLine: false,
          highlightActiveLineGutter: false,
        }}
        className="markdown-live-editor"
        minHeight="60vh"
      />

      {historyOpen && (
        <VersionHistoryPanel
          relativePath={page.relativePath}
          onClose={() => setHistoryOpen(false)}
          onRestored={() => void reloadLatest()}
        />
      )}
    </div>
  );
}
