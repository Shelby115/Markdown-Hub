import { useEffect, useRef, useState } from "react";
import {
  AiTemplateMode,
  AiTemplateParseResult,
  AiTemplateSlotValue,
  api,
  extractErrorMessage,
} from "../api/client";
import { assembleDocument, groupSlots } from "./aiTemplate";
import { ThinkingIndicator } from "./ThinkingIndicator";

interface SlotState {
  id: string;
  name: string;
  index: number;
  count: number;
  content: string;
  locked: boolean;
  busy: boolean;
  warnings: string[];
  error: string | null;
}

export function AiTemplatePanel({
  templatePath,
  initialPagePath,
  pathEditable = false,
  parsed,
  onCancel,
  onSave,
}: {
  templatePath: string;
  /** Where the finished page will be saved - shown as an editable field when pathEditable. */
  initialPagePath: string;
  pathEditable?: boolean;
  parsed: AiTemplateParseResult;
  onCancel: () => void;
  onSave: (content: string, pagePath: string) => void;
}) {
  const [slots, setSlots] = useState<SlotState[]>(() =>
    parsed.slots.map((s) => ({ ...s, content: "", locked: false, busy: false, warnings: [], error: null }))
  );
  const [variables, setVariables] = useState<Record<string, string>>(() =>
    Object.fromEntries(parsed.fillInVariables.map((v) => [v, ""]))
  );
  const [pagePath, setPagePath] = useState(initialPagePath);
  const [generatingAll, setGeneratingAll] = useState(false);
  const [aiAvailable, setAiAvailable] = useState<boolean | null>(null);

  // Generation runs as a sequential loop that needs each slot's *current* content as context, so
  // the loop reads from a ref rather than from state it can't see updated until the next render.
  const slotsRef = useRef(slots);
  const stopRef = useRef(false);
  const autoStartedRef = useRef(false);

  const writeSlots = (next: SlotState[]) => {
    slotsRef.current = next;
    setSlots(next);
  };

  const updateSlot = (id: string, patch: Partial<SlotState>) => {
    writeSlots(slotsRef.current.map((s) => (s.id === id ? { ...s, ...patch } : s)));
  };

  const currentValues = (): AiTemplateSlotValue[] =>
    slotsRef.current.map((s) => ({ id: s.id, content: s.content, locked: s.locked }));

  /** Returns false if the request failed, so a batch can stop instead of hammering a dead service. */
  const generateSlot = async (id: string, mode: AiTemplateMode): Promise<boolean> => {
    updateSlot(id, { busy: true, error: null });
    try {
      const result = await api.aiTemplateGenerate(templatePath, id, mode, currentValues());
      // Content is only replaced on success - a failed reroll must never destroy a good result.
      updateSlot(id, { content: result.content, warnings: result.warnings, busy: false });
      return true;
    } catch (err) {
      updateSlot(id, { busy: false, error: extractErrorMessage(err, "The AI request failed.") });
      return false;
    }
  };

  const generateMany = async (ids: string[]) => {
    stopRef.current = false;
    setGeneratingAll(true);
    for (const id of ids) {
      if (stopRef.current) break;
      const slot = slotsRef.current.find((s) => s.id === id);
      if (!slot || slot.locked) continue;
      if (!(await generateSlot(id, "Generate"))) break;
    }
    setGeneratingAll(false);
  };

  // Opening the panel is itself the request to generate - there's nothing useful to look at
  // until the first pass has run, so don't make the user ask for it a second time.
  useEffect(() => {
    api
      .getAiAssistantStatus()
      .then((status) => {
        setAiAvailable(status.available);
        if (status.available && !autoStartedRef.current) {
          autoStartedRef.current = true;
          void generateMany(slotsRef.current.map((s) => s.id));
        }
      })
      .catch(() => setAiAvailable(false));
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const busy = generatingAll || slots.some((s) => s.busy);
  const groups = groupSlots(parsed.slots);
  const contents = Object.fromEntries(slots.map((s) => [s.id, s.content]));
  const hasContent = slots.some((s) => s.content.trim().length > 0);
  const savePath = pagePath.trim();

  return (
    <div className="modal-overlay" onClick={busy ? undefined : onCancel}>
      <div className="modal ai-template-modal" onClick={(e) => e.stopPropagation()}>
        <h2>Generate “{savePath.split("/").pop() || "new page"}”</h2>

        {aiAvailable === false ? (
          <p className="muted">
            Ollama installation not found. Please install Ollama and an AI model to integrate. Set{" "}
            <code>OLLAMA_BASE_URL</code> and <code>OLLAMA_MODEL</code> in your docker-compose environment.
          </p>
        ) : (
          <div className="ai-template-body">
            {parsed.fillInVariables.length > 0 && (
              <section className="modal-fields">
                {parsed.fillInVariables.map((name) => (
                  <label key={name} className="modal-field">
                    <span>{name}</span>
                    <input
                      value={variables[name]}
                      onChange={(e) => setVariables((prev) => ({ ...prev, [name]: e.target.value }))}
                    />
                  </label>
                ))}
              </section>
            )}

            {groups.map((group) => (
              <section key={group.name} className="ai-template-group">
                {/* A single-slot group would just repeat its own card's title. */}
                {group.slots.length > 1 && (
                  <div className="ai-template-group-header">
                    <h3>{group.name}</h3>
                    <button className="secondary" disabled={busy} onClick={() => void generateMany(group.slots.map((s) => s.id))}>
                      Reroll all
                    </button>
                  </div>
                )}

                {group.slots.map((slot) => {
                  const state = slots.find((s) => s.id === slot.id)!;
                  return (
                    <div key={slot.id} className="ai-result-card ai-template-card">
                      <div className="ai-result-card-header">
                        <span className="ai-result-card-title">
                          {slot.count > 1 ? `${slot.name} ${slot.index}` : slot.name}
                          {state.locked && " 🔒"}
                        </span>
                        <div className="ai-result-card-header-actions">
                          <button
                            className="icon-button"
                            aria-label="Reroll"
                            title="Reroll"
                            disabled={busy || state.locked}
                            onClick={() => void generateSlot(slot.id, "Generate")}
                          >
                            🎲
                          </button>
                          <button
                            className="icon-button"
                            aria-label="Improve"
                            title="Improve"
                            disabled={busy || state.locked || !state.content.trim()}
                            onClick={() => void generateSlot(slot.id, "Improve")}
                          >
                            ✨
                          </button>
                          <button
                            className="icon-button"
                            aria-label={state.locked ? "Unlock" : "Lock"}
                            title={state.locked ? "Unlock" : "Lock so generation keeps it"}
                            onClick={() => updateSlot(slot.id, { locked: !state.locked })}
                          >
                            {state.locked ? "🔒" : "🔓"}
                          </button>
                        </div>
                      </div>

                      <textarea
                        className="ai-result-card-textarea"
                        aria-label={slot.id}
                        placeholder={state.busy ? "Generating…" : "Not generated yet."}
                        value={state.content}
                        readOnly={state.locked}
                        onChange={(e) => updateSlot(slot.id, { content: e.target.value })}
                      />

                      {state.busy && (
                        <p className="muted">
                          <ThinkingIndicator />
                        </p>
                      )}
                      {state.error && <div className="banner banner-error">{state.error}</div>}
                      {state.warnings.map((w) => (
                        <div key={w} className="ai-template-warning">
                          ⚠ {w}
                        </div>
                      ))}
                    </div>
                  );
                })}
              </section>
            ))}

            {pathEditable && (
              <label className="modal-field ai-template-path">
                <span>Save as</span>
                <input value={pagePath} onChange={(e) => setPagePath(e.target.value)} placeholder="Folder/Page name" />
              </label>
            )}
          </div>
        )}

        <div className="modal-actions">
          <button className="secondary" onClick={onCancel} disabled={busy}>
            Cancel
          </button>
          {generatingAll ? (
            <button className="secondary" onClick={() => (stopRef.current = true)}>
              Stop
            </button>
          ) : (
            <button
              className="secondary"
              disabled={busy || aiAvailable === false || slots.length === 0}
              onClick={() => void generateMany(slots.map((s) => s.id))}
            >
              Regenerate all
            </button>
          )}
          <button
            disabled={busy || !hasContent || savePath.length === 0}
            onClick={() => onSave(assembleDocument(parsed.elements, contents, variables), savePath)}
          >
            Save as page
          </button>
        </div>
      </div>
    </div>
  );
}
