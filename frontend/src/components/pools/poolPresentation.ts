import { GenerationPool, GenerationPoolStatus } from "../../api/client";

/** Every variant colours the same six server statuses, so the mapping lives in one place. */
export type PoolTone = "active" | "ok" | "warn" | "muted" | "neutral";

const TONES: Record<string, PoolTone> = {
  Generating: "active",
  Full: "ok",
  Queued: "neutral",
  Paused: "warn",
  Waiting: "warn",
  Off: "muted",
};

export const toneOf = (status: string): PoolTone => TONES[status] ?? "neutral";

export const percentFull = (pool: GenerationPool): number =>
  pool.targetCount === 0 ? 0 : Math.min(100, Math.round((pool.readyCount / pool.targetCount) * 100));

/** The one-line answer to "what is the generator doing right now?" */
export function generatorHeadline(status: GenerationPoolStatus | null): { text: string; tone: PoolTone } {
  if (!status) {
    return { text: "Loading…", tone: "muted" };
  }
  if (status.generatingPoolName) {
    return { text: `Writing an entry for “${status.generatingPoolName}”`, tone: "active" };
  }
  if (status.settings.paused) {
    return { text: "Paused", tone: "warn" };
  }
  if (!status.runningNow) {
    return { text: "Outside the allowed window", tone: "warn" };
  }
  return { text: "Idle - waiting for a pool to top up", tone: "ok" };
}
