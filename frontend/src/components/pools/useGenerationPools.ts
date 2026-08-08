import { useEffect, useRef, useState } from "react";
import {
  GenerationPool,
  GenerationPoolEntry,
  GenerationPoolSettings,
  GenerationPoolStatus,
  api,
  extractErrorMessage,
} from "../../api/client";

export interface PoolForm {
  name: string;
  instructions: string;
  targetCount: number;
  enabled: boolean;
}

export const BLANK_POOL: PoolForm = {
  name: "",
  instructions: "- Describe what one entry should be.\n- Max words: 40\n",
  targetCount: 20,
  enabled: false,
};

/** How often the page refreshes counts and status while it's open. */
const POLL_MS = 5000;

/** "new" means the form is creating a pool rather than editing an existing one. */
export type PoolSelection = number | "new" | null;

export interface GenerationPoolsController {
  status: GenerationPoolStatus | null;
  settingsForm: GenerationPoolSettings | null;
  setSettingsForm: (settings: GenerationPoolSettings) => void;
  pools: GenerationPool[] | null;
  entries: GenerationPoolEntry[];
  selected: PoolSelection;
  poolForm: PoolForm;
  setPoolForm: (form: PoolForm) => void;
  busy: boolean;
  error: string | null;
  notice: string | null;
  selectPool: (pool: GenerationPool) => void;
  startNewPool: () => void;
  closeEditor: () => void;
  savePool: () => void;
  deletePool: (pool: GenerationPool) => void;
  generateOne: (poolId: number) => void;
  forget: (entryId: number) => void;
  saveSettings: (settings: GenerationPoolSettings) => void;
  togglePause: () => void;
}

/**
 * All of the generation-pool page's data and actions, with no opinion about how any of it looks.
 * Extracted so several presentations can share one implementation rather than each re-deriving
 * polling, busy state, and error handling.
 */
export function useGenerationPools(): GenerationPoolsController {
  const [status, setStatus] = useState<GenerationPoolStatus | null>(null);
  const [settingsForm, setSettingsForm] = useState<GenerationPoolSettings | null>(null);
  const [pools, setPools] = useState<GenerationPool[] | null>(null);
  const [selected, setSelected] = useState<PoolSelection>(null);
  const [poolForm, setPoolForm] = useState<PoolForm>(BLANK_POOL);
  const [entries, setEntries] = useState<GenerationPoolEntry[]>([]);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [notice, setNotice] = useState<string | null>(null);

  // The poll runs on a timer, so it reads current state through refs rather than closing over
  // whatever the values were when the effect first ran.
  const busyRef = useRef(busy);
  const selectedRef = useRef(selected);
  const entriesRef = useRef(entries);
  busyRef.current = busy;
  selectedRef.current = selected;
  entriesRef.current = entries;

  const loadStatus = async () => {
    const loaded = await api.adminGetPoolSettings();
    setStatus(loaded);
    return loaded;
  };

  const loadPools = async () => {
    const loaded = await api.adminGetPools();
    setPools(loaded);
    return loaded;
  };

  useEffect(() => {
    void loadStatus()
      .then((loaded) => setSettingsForm(loaded.settings))
      .catch((err) => setError(extractErrorMessage(err, "Could not load generator settings.")));
    void loadPools().catch((err) => setError(extractErrorMessage(err, "Could not load pools.")));
  }, []);

  // Background generation happens without the user doing anything, so the page has to go and look.
  // Skipped while an action is in flight, and it never touches the edit form - only counts/status.
  useEffect(() => {
    const timer = setInterval(() => {
      if (busyRef.current) return;
      void (async () => {
        try {
          await loadStatus();
          const refreshed = await loadPools();
          const open = selectedRef.current;
          if (typeof open === "number") {
            const pool = refreshed.find((p) => p.id === open);
            if (pool && pool.readyCount !== entriesRef.current.length) {
              setEntries(await api.adminGetPoolEntries(open));
            }
          }
        } catch {
          // A transient poll failure isn't worth an error banner - the next tick retries.
        }
      })();
    }, POLL_MS);
    return () => clearInterval(timer);
  }, []);

  /** Wraps an action with the shared busy/error handling every button here needs. */
  const run = async (action: () => Promise<void>) => {
    setBusy(true);
    setError(null);
    setNotice(null);
    try {
      await action();
    } catch (err) {
      setError(extractErrorMessage(err, "That didn't work."));
    } finally {
      setBusy(false);
    }
  };

  const saveSettings = (settings: GenerationPoolSettings) =>
    void run(async () => {
      const saved = await api.adminSetPoolSettings(settings);
      setStatus(saved);
      setSettingsForm(saved.settings);
      await loadPools(); // every pool's status depends on these
    });

  return {
    status,
    settingsForm,
    setSettingsForm,
    pools,
    entries,
    selected,
    poolForm,
    setPoolForm,
    busy,
    error,
    notice,
    saveSettings,

    togglePause: () => {
      if (settingsForm && status) {
        saveSettings({ ...settingsForm, paused: !status.settings.paused });
      }
    },

    selectPool: (pool) =>
      void run(async () => {
        setSelected(pool.id);
        setPoolForm({
          name: pool.name,
          instructions: pool.instructions,
          targetCount: pool.targetCount,
          enabled: pool.enabled,
        });
        setEntries(await api.adminGetPoolEntries(pool.id));
      }),

    startNewPool: () => {
      setSelected("new");
      setPoolForm(BLANK_POOL);
      setEntries([]);
      setError(null);
      setNotice(null);
    },

    closeEditor: () => setSelected(null),

    savePool: () =>
      void run(async () => {
        const saved =
          selected === "new"
            ? await api.adminCreatePool(poolForm)
            : await api.adminUpdatePool(selected as number, poolForm);
        await loadPools();
        setSelected(saved.id);
        setEntries(await api.adminGetPoolEntries(saved.id));
      }),

    deletePool: (pool) =>
      void run(async () => {
        if (!window.confirm(`Delete the “${pool.name}” pool and everything in it?`)) return;
        await api.adminDeletePool(pool.id);
        setSelected(null);
        setEntries([]);
        await loadPools();
      }),

    generateOne: (poolId) =>
      void run(async () => {
        const entry = await api.adminGeneratePoolEntry(poolId);
        setEntries((prev) => [entry, ...prev]);
        setNotice("Added one entry.");
        await loadPools();
      }),

    forget: (entryId) =>
      void run(async () => {
        await api.aiPoolForgetEntry(entryId);
        setEntries((prev) => prev.filter((e) => e.id !== entryId));
        await loadPools();
      }),
  };
}
