import { useEffect, useRef, useState } from "react";
import { AssistantAction, AssistantResultCard, api } from "../api/client";
import { ThinkingIndicator } from "./ThinkingIndicator";

interface ResultCardState extends AssistantResultCard {
  id: string;
  editing: boolean;
  editedContent: string;
  added: boolean;
}

const ACTIONS: { action: AssistantAction; label: string; requiresQuestion?: boolean }[] = [
  { action: "Summarize", label: "Summarize" },
  { action: "ExpandTopic", label: "Expand Topic" },
  { action: "Ask", label: "Ask", requiresQuestion: true },
];

export function AiAssistantPanel({
  currentPagePath,
  collapsed,
  onCollapsedChange,
  onContentAddedToCurrentPage,
}: {
  currentPagePath: string | null;
  collapsed: boolean;
  onCollapsedChange: (collapsed: boolean) => void;
  onContentAddedToCurrentPage: () => void;
}) {
  const [aiAvailable, setAiAvailable] = useState<boolean | null>(null);
  const [contextPaths, setContextPaths] = useState<string[]>([]);
  const [pickerQuery, setPickerQuery] = useState("");
  const [pickerResults, setPickerResults] = useState<{ relativePath: string; pageName: string }[]>([]);
  const [question, setQuestion] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [results, setResults] = useState<ResultCardState[]>([]);
  const nextCardId = useRef(0);

  // Checked once per session (the panel stays mounted throughout, see below) rather than
  // re-checked on every open - if Ollama comes online mid-session, a page refresh picks it up.
  useEffect(() => {
    api
      .getAiAssistantStatus()
      .then((status) => setAiAvailable(status.available))
      .catch(() => setAiAvailable(false));
  }, []);

  // The panel stays mounted for the whole session (so results/context survive collapsing it),
  // so context can't just be seeded once at mount the way it could when the panel was
  // conditionally rendered. Instead, default to the current page whenever the panel becomes
  // visible with no context chosen yet - covers both the expand button and (in tests, or if
  // this ever mounts already expanded) rendering directly expanded.
  useEffect(() => {
    if (!collapsed && contextPaths.length === 0 && currentPagePath) {
      setContextPaths([currentPagePath]);
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [collapsed]);

  useEffect(() => {
    if (!pickerQuery.trim()) {
      setPickerResults([]);
      return;
    }
    const handle = window.setTimeout(() => {
      api
        .suggestWikiLinks(pickerQuery)
        .then((hits) => setPickerResults(hits.filter((h) => !contextPaths.includes(h.relativePath))))
        .catch(() => setPickerResults([]));
    }, 200);
    return () => window.clearTimeout(handle);
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [pickerQuery]);

  const addContext = (relativePath: string) => {
    setContextPaths((prev) => (prev.includes(relativePath) ? prev : [...prev, relativePath]));
    setPickerQuery("");
    setPickerResults([]);
  };

  const removeContext = (relativePath: string) => {
    setContextPaths((prev) => prev.filter((p) => p !== relativePath));
  };

  const runAction = async (action: AssistantAction) => {
    if (contextPaths.length === 0) {
      setError("Select at least one page as context first.");
      return;
    }
    if (action === "Ask" && !question.trim()) {
      setError("Enter a question first.");
      return;
    }
    setBusy(true);
    setError(null);
    try {
      const { results: cards } = await api.aiAssistant(action, question.trim() || null, contextPaths);
      setResults((prev) => [
        ...cards.map((c) => ({ ...c, id: `card-${nextCardId.current++}`, editing: false, editedContent: c.content, added: false })),
        ...prev,
      ]);
    } catch (err: any) {
      try {
        setError(JSON.parse(err.message)?.message ?? "The AI request failed.");
      } catch {
        setError("The AI request failed.");
      }
    } finally {
      setBusy(false);
    }
  };

  const toggleEdit = (id: string) => {
    setResults((prev) => prev.map((c) => (c.id === id ? { ...c, editing: !c.editing } : c)));
  };

  const updateEditedContent = (id: string, value: string) => {
    setResults((prev) => prev.map((c) => (c.id === id ? { ...c, editedContent: value } : c)));
  };

  const ignoreCard = (id: string) => {
    setResults((prev) => prev.filter((c) => c.id !== id));
  };

  const addToCurrentPage = async (card: ResultCardState) => {
    if (!currentPagePath) return;
    setError(null);
    try {
      const fresh = await api.getPage(currentPagePath);
      const separator = fresh.content.endsWith("\n") || fresh.content.length === 0 ? "\n" : "\n\n";
      const newContent = `${fresh.content}${separator}${card.editedContent}\n`;
      await api.savePage(currentPagePath, newContent, fresh.lastModifiedUtc);
      setResults((prev) => prev.map((c) => (c.id === card.id ? { ...c, added: true } : c)));
      onContentAddedToCurrentPage();
    } catch {
      setError("Couldn't add that to the page - it may have changed since you loaded it.");
    }
  };

  if (collapsed) {
    return (
      <button className="icon-button ai-assistant-expand-button" title="Show AI Assistant" onClick={() => onCollapsedChange(false)}>
        «
      </button>
    );
  }

  return (
    <aside className="ai-assistant-panel">
      <div className="ai-assistant-header">
        <span className="brand">AI Assistant</span>
        <button className="icon-button" title="Hide AI Assistant" onClick={() => onCollapsedChange(true)}>
          »
        </button>
      </div>

      {aiAvailable === false ? (
        <div className="ai-assistant-body">
          <p className="muted">
            Ollama installation not found. Please install Ollama and an AI model to integrate.
            Set <code>OLLAMA_BASE_URL</code> and <code>OLLAMA_MODEL</code> in your docker-compose
            environment.
          </p>
        </div>
      ) : aiAvailable === null ? (
        <div className="ai-assistant-body">
          <p className="muted">Checking AI availability…</p>
        </div>
      ) : (
      <div className="ai-assistant-body">
        <section className="ai-assistant-section">
          <h3>Context</h3>
          <ul className="ai-context-list">
            {contextPaths.map((p) => (
              <li key={p}>
                <span>{p}</span>
                <button className="icon-button" title="Remove from context" onClick={() => removeContext(p)}>
                  ✕
                </button>
              </li>
            ))}
            {contextPaths.length === 0 && <li className="muted">No pages selected yet.</li>}
          </ul>
          <div className="ai-context-picker">
            <input
              type="text"
              placeholder="Add a page as context…"
              value={pickerQuery}
              onChange={(e) => setPickerQuery(e.target.value)}
            />
            {pickerResults.length > 0 && (
              <ul className="ai-context-picker-results">
                {pickerResults.map((r) => (
                  <li key={r.relativePath} onClick={() => addContext(r.relativePath)}>
                    {r.pageName}
                  </li>
                ))}
              </ul>
            )}
          </div>
        </section>

        <section className="ai-assistant-section">
          <textarea
            className="ai-assistant-question"
            placeholder="Ask the assistant…"
            value={question}
            onChange={(e) => setQuestion(e.target.value)}
          />
          <div className="ai-action-group">
            {ACTIONS.map(({ action, label }) => (
              <button key={action} className="ai-action-group-button" disabled={busy} onClick={() => void runAction(action)}>
                {label}
              </button>
            ))}
          </div>
        </section>

        {error && <div className="banner banner-error">{error}</div>}
        {busy && (
          <p className="muted">
            <ThinkingIndicator />
          </p>
        )}

        <section className="ai-assistant-section">
          <h3>Suggestions</h3>
          {results.length === 0 && !busy && <p className="muted">Results will appear here.</p>}
          {results.map((card) => (
            <div key={card.id} className="ai-result-card">
              <div className="ai-result-card-header">
                <span className="ai-result-card-title">{card.title}</span>
                <div className="ai-result-card-header-actions">
                  {card.added ? (
                    <span className="muted">Added ✓</span>
                  ) : (
                    <>
                      <button
                        className="icon-button"
                        aria-label="Add to Page"
                        title={currentPagePath ? "Add to page" : "Open a page first"}
                        disabled={!currentPagePath}
                        onClick={() => void addToCurrentPage(card)}
                      >
                        ➕
                      </button>
                      <button
                        className="icon-button"
                        aria-label={card.editing ? "Done" : "Edit"}
                        title={card.editing ? "Done editing" : "Edit"}
                        onClick={() => toggleEdit(card.id)}
                      >
                        {card.editing ? "✓" : "✎"}
                      </button>
                      <button className="icon-button" aria-label="Delete" title="Delete" onClick={() => ignoreCard(card.id)}>
                        ✕
                      </button>
                    </>
                  )}
                </div>
              </div>
              {card.editing ? (
                <textarea
                  className="ai-result-card-textarea"
                  value={card.editedContent}
                  onChange={(e) => updateEditedContent(card.id, e.target.value)}
                />
              ) : (
                <div className="ai-result-card-content">{card.editedContent}</div>
              )}
            </div>
          ))}
        </section>
      </div>
      )}
    </aside>
  );
}
