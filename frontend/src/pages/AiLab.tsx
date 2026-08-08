import { useEffect, useState } from "react";
import { Link } from "react-router-dom";
import { AiSettings, api, extractErrorMessage } from "../api/client";
import { GenerationPools } from "../components/pools/GenerationPools";
import { useGenerationPools } from "../components/pools/useGenerationPools";
import "../styles/ai-lab.css";

/**
 * Everything AI in one place: which model the app talks to, and the generation pools that
 * pre-write template content against it. Both were previously buried in the middle of the Admin
 * page between user management and folder permissions, which is not where anyone looks for them.
 */
export function AiLab() {
  const pools = useGenerationPools();

  const [settings, setSettings] = useState<AiSettings | null>(null);
  const [models, setModels] = useState<string[] | null>(null);
  const [modelsError, setModelsError] = useState<string | null>(null);
  const [modelInput, setModelInput] = useState("");
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void (async () => {
      try {
        const loaded = await api.adminGetAiSettings();
        setSettings(loaded);
        setModelInput(loaded.selectedModel ?? "");
      } catch (err) {
        setError(extractErrorMessage(err, "Couldn't load AI settings."));
      }
      // Listing installed models can fail independently (e.g. Ollama unreachable) without
      // blocking the settings above - a manual model name can still be typed in either way.
      try {
        const { models: installed } = await api.adminListAiModels();
        setModels(installed);
        setModelsError(null);
      } catch (err) {
        setModelsError(extractErrorMessage(err, "Couldn't list installed Ollama models."));
      }
    })();
  }, []);

  const setModel = async (model: string | null) => {
    setBusy(true);
    setError(null);
    try {
      const saved = await api.adminSetAiModel(model);
      setSettings(saved);
      setModelInput(saved.selectedModel ?? "");
    } catch (err) {
      setError(extractErrorMessage(err, "Couldn't save the AI model."));
    } finally {
      setBusy(false);
    }
  };

  return (
    <div className="admin-page ai-lab">
      <div className="ai-lab-head">
        <h1>AI</h1>
        <Link to="/admin">← Admin</Link>
      </div>

      {error && <div className="banner banner-error">{error}</div>}

      <section className="ai-lab-section">
        <h2>Model</h2>
        {!settings ? (
          <p className="muted">Loading…</p>
        ) : (
          <>
            <p className="muted">
              Currently using <strong>{settings.effectiveModel}</strong>
              {!settings.selectedModel && " (configured default - no override set)"}. Applies to AI-assisted editing,
              the AI assistant, and pool generation, for every user.
            </p>
            {modelsError && (
              <div className="banner banner-warning">{modelsError} You can still type a model name manually below.</div>
            )}
            <div className="ai-lab-model-form">
              <input
                list="ai-model-options"
                type="text"
                aria-label="Ollama model"
                placeholder={settings.configuredDefaultModel}
                value={modelInput}
                onChange={(e) => setModelInput(e.target.value)}
              />
              {models && (
                <datalist id="ai-model-options">
                  {models.map((m) => (
                    <option key={m} value={m} />
                  ))}
                </datalist>
              )}
              <button disabled={busy} onClick={() => void setModel(modelInput.trim() || null)}>
                Save
              </button>
              <button className="secondary" disabled={busy || !settings.selectedModel} onClick={() => void setModel(null)}>
                Reset to default
              </button>
            </div>
          </>
        )}
      </section>

      <section className="ai-lab-section">
        <h2>Generation pools</h2>
        <GenerationPools c={pools} />
      </section>
    </div>
  );
}
