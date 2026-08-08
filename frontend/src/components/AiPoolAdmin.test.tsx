import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AiPoolAdmin } from "./AiPoolAdmin";

const mocks = vi.hoisted(() => ({
  getPools: vi.fn(),
  createPool: vi.fn(),
  updatePool: vi.fn(),
  deletePool: vi.fn(),
  generateEntry: vi.fn(),
  getEntries: vi.fn(),
  getSettings: vi.fn(),
  setSettings: vi.fn(),
  forgetEntry: vi.fn(),
}));

vi.mock("../api/client", async () => {
  const actual = await vi.importActual<typeof import("../api/client")>("../api/client");
  return {
    ...actual,
    api: {
      adminGetPools: mocks.getPools,
      adminCreatePool: mocks.createPool,
      adminUpdatePool: mocks.updatePool,
      adminDeletePool: mocks.deletePool,
      adminGeneratePoolEntry: mocks.generateEntry,
      adminGetPoolEntries: mocks.getEntries,
      adminGetPoolSettings: mocks.getSettings,
      adminSetPoolSettings: mocks.setSettings,
      aiPoolForgetEntry: mocks.forgetEntry,
    },
  };
});

const pool = {
  id: 1,
  name: "Interactible",
  instructions: "- One brief interactible.",
  targetCount: 20,
  enabled: true,
  readyCount: 3,
  status: "Waiting",
  statusReason: "17 more to generate, but generation is only allowed between 22:00-06:00 UTC. It is now 14:30 UTC.",
  updatedAtUtc: "2026-08-07T10:00:00Z",
};

const settings = {
  paused: false,
  windowStartUtc: "22:00",
  windowEndUtc: "06:00",
  intervalSeconds: 60,
  usedEntryRetentionDays: 90,
};

const status = {
  settings,
  runningNow: false,
  reason: "Generation is only allowed between 22:00-06:00 UTC, and it is now 14:30 UTC.",
  generatingPoolName: null as string | null,
  nowUtc: "14:30",
};

describe("AiPoolAdmin", () => {
  beforeEach(() => {
    mocks.getPools.mockReset().mockResolvedValue([pool]);
    mocks.getSettings.mockReset().mockResolvedValue(status);
    mocks.setSettings.mockReset().mockImplementation((s) => Promise.resolve({ ...status, settings: s }));
    mocks.getEntries.mockReset().mockResolvedValue([{ id: 5, content: "A rusted lantern.", status: "Ready", createdAtUtc: "" }]);
    mocks.generateEntry.mockReset();
    mocks.updatePool.mockReset().mockResolvedValue(pool);
    mocks.createPool.mockReset().mockResolvedValue({ ...pool, id: 2, name: "NPC Name" });
    mocks.forgetEntry.mockReset().mockResolvedValue(undefined);
  });

  it("says why the generator isn't running rather than just that it isn't", async () => {
    render(<AiPoolAdmin />);

    expect(await screen.findByText("Outside the allowed window")).toBeInTheDocument();
    expect(screen.getByText(/only allowed between 22:00-06:00 UTC/)).toBeInTheDocument();
  });

  it("shows each pool's ready count against its target", async () => {
    render(<AiPoolAdmin />);

    expect(await screen.findByText("3 / 20")).toBeInTheDocument();
  });

  it("a pool's status badge carries the explanation as a tooltip", async () => {
    render(<AiPoolAdmin />);

    const badge = await screen.findByTitle(/17 more to generate/);
    expect(badge).toHaveTextContent("Waiting");
  });

  it("names the pool being written while the generator is working", async () => {
    mocks.getSettings.mockResolvedValue({ ...status, generatingPoolName: "Interactible", runningNow: true });
    mocks.getPools.mockResolvedValue([{ ...pool, status: "Generating", statusReason: "Writing a new entry right now." }]);
    render(<AiPoolAdmin />);

    expect(await screen.findByText(/Writing an entry for “Interactible”/)).toBeInTheDocument();
    expect(await screen.findByTitle("Writing a new entry right now.")).toHaveTextContent("Generating");
  });

  it("picks up entries the background generator added, without the user doing anything", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      render(<AiPoolAdmin />);
      expect(await screen.findByText("3 / 20")).toBeInTheDocument();

      mocks.getPools.mockResolvedValue([{ ...pool, readyCount: 4 }]);
      await vi.advanceTimersByTimeAsync(5000);

      await waitFor(() => expect(screen.getByText("4 / 20")).toBeInTheDocument());
    } finally {
      vi.useRealTimers();
    }
  });

  it("pausing sends the current settings with paused flipped", async () => {
    const user = userEvent.setup();
    render(<AiPoolAdmin />);

    await user.click(await screen.findByText("Pause"));

    await waitFor(() => expect(mocks.setSettings).toHaveBeenCalledWith({ ...settings, paused: true }));
    expect(await screen.findByText("Resume")).toBeInTheDocument();
  });

  it("editing a pool loads its prompt and ready entries, and saves changes", async () => {
    const user = userEvent.setup();
    render(<AiPoolAdmin />);

    await user.click(await screen.findByText("Edit"));

    expect(await screen.findByLabelText(/Pool prompt/)).toHaveValue("- One brief interactible.");
    expect(screen.getByText("A rusted lantern.")).toBeInTheDocument();

    await user.click(screen.getByText("Save pool"));

    await waitFor(() =>
      expect(mocks.updatePool).toHaveBeenCalledWith(1, {
        name: "Interactible",
        instructions: "- One brief interactible.",
        targetCount: 20,
        enabled: true,
      })
    );
  });

  it("forgetting an entry removes it from the list", async () => {
    const user = userEvent.setup();
    render(<AiPoolAdmin />);
    await user.click(await screen.findByText("Edit"));

    await user.click(await screen.findByText("Forget"));

    await waitFor(() => expect(mocks.forgetEntry).toHaveBeenCalledWith(5));
    expect(screen.queryByText("A rusted lantern.")).not.toBeInTheDocument();
  });

  it("surfaces a rejected prompt rather than failing silently", async () => {
    const user = userEvent.setup();
    render(<AiPoolAdmin />);
    await user.click(await screen.findByText("Edit"));

    mocks.generateEntry.mockRejectedValueOnce(new Error(JSON.stringify({ message: "Ollama is unreachable." })));
    await user.click(screen.getByText("Generate one now"));

    expect(await screen.findByText("Ollama is unreachable.")).toBeInTheDocument();
  });
});
