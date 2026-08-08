import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { PoolsCards } from "./PoolsCards";
import { PoolsConsole } from "./PoolsConsole";
import { PoolsShelf } from "./PoolsShelf";
import { PoolsSplit } from "./PoolsSplit";
import { GenerationPoolsController } from "./useGenerationPools";

/**
 * TEMPORARY - covers the four pool-design-lab layouts. Whichever one is picked keeps its cases
 * here; this file goes away with the other three. They're presentation only, so a hand-built
 * controller is enough - useGenerationPools is already covered through AiPoolAdmin.
 */
const pools = [
  {
    id: 1,
    name: "Interactible",
    instructions: "- One brief interactible.",
    targetCount: 20,
    enabled: true,
    readyCount: 6,
    status: "Waiting",
    statusReason: "14 more to generate, but generation is only allowed between 22:00-06:00 UTC.",
    updatedAtUtc: "2026-08-08T10:00:00Z",
  },
  {
    id: 2,
    name: "NPC Name",
    instructions: "- One name.",
    targetCount: 50,
    enabled: true,
    readyCount: 50,
    status: "Full",
    statusReason: "All 50 entries are ready.",
    updatedAtUtc: "2026-08-08T10:00:00Z",
  },
];

function controller(overrides: Partial<GenerationPoolsController> = {}): GenerationPoolsController {
  return {
    status: {
      settings: {
        paused: false,
        windowStartUtc: "22:00",
        windowEndUtc: "06:00",
        intervalSeconds: 60,
        usedEntryRetentionDays: 90,
      },
      runningNow: false,
      reason: "Generation is only allowed between 22:00-06:00 UTC, and it is now 14:30 UTC.",
      generatingPoolName: null,
      nowUtc: "14:30",
    },
    settingsForm: {
      paused: false,
      windowStartUtc: "22:00",
      windowEndUtc: "06:00",
      intervalSeconds: 60,
      usedEntryRetentionDays: 90,
    },
    setSettingsForm: vi.fn(),
    pools,
    entries: [{ id: 9, content: "A rusted lantern.", status: "Ready", createdAtUtc: "" }],
    selected: null,
    poolForm: { name: "Interactible", instructions: "- One brief interactible.", targetCount: 20, enabled: true },
    setPoolForm: vi.fn(),
    busy: false,
    error: null,
    notice: null,
    selectPool: vi.fn(),
    startNewPool: vi.fn(),
    closeEditor: vi.fn(),
    savePool: vi.fn(),
    deletePool: vi.fn(),
    generateOne: vi.fn(),
    forget: vi.fn(),
    saveSettings: vi.fn(),
    togglePause: vi.fn(),
    ...overrides,
  };
}

const VARIANTS = [
  { name: "console", render: (c: GenerationPoolsController) => <PoolsConsole c={c} /> },
  { name: "cards", render: (c: GenerationPoolsController) => <PoolsCards c={c} /> },
  { name: "split", render: (c: GenerationPoolsController) => <PoolsSplit c={c} /> },
  { name: "shelf", render: (c: GenerationPoolsController) => <PoolsShelf c={c} /> },
];

describe.each(VARIANTS)("pool layout: $name", ({ render: renderVariant }) => {
  it("lists every pool with its status and the explanation as a tooltip", () => {
    render(renderVariant(controller()));

    expect(screen.getByText("Interactible")).toBeInTheDocument();
    expect(screen.getByText("NPC Name")).toBeInTheDocument();
    expect(screen.getByTitle(/14 more to generate/)).toHaveTextContent("Waiting");
    expect(screen.getByTitle("All 50 entries are ready.")).toHaveTextContent("Full");
  });

  it("says why the generator isn't running", () => {
    render(renderVariant(controller()));

    expect(screen.getByText(/only allowed between 22:00-06:00 UTC/)).toBeInTheDocument();
  });

  it("pausing goes through the controller", async () => {
    const user = userEvent.setup();
    const c = controller();
    render(renderVariant(c));

    await user.click(screen.getByRole("button", { name: /^Pause/ }));

    expect(c.togglePause).toHaveBeenCalled();
  });

  it("opens a pool for editing", async () => {
    const user = userEvent.setup();
    const c = controller();
    render(renderVariant(c));

    // Each layout opens a pool differently - by an Edit button, or by clicking the row itself.
    const edit = screen.queryAllByRole("button", { name: /^Edit$/ })[0];
    await user.click(edit ?? screen.getByText("Interactible").closest("button, div")!);

    expect(c.selectPool).toHaveBeenCalledWith(pools[0]);
  });

  it("shows the prompt and ready entries once a pool is selected", () => {
    render(renderVariant(controller({ selected: 1 })));

    expect(screen.getByLabelText("Pool prompt")).toHaveValue("- One brief interactible.");
    const entry = screen.getByText("A rusted lantern.");
    expect(within(entry.parentElement!).getByRole("button", { name: /forget/i })).toBeInTheDocument();
  });

  it("marks the pool currently being written", () => {
    const generating = [{ ...pools[0], status: "Generating", statusReason: "Writing a new entry right now." }, pools[1]];
    render(
      renderVariant(
        controller({
          pools: generating,
          status: { ...controller().status!, generatingPoolName: "Interactible" },
        })
      )
    );

    expect(screen.getByText(/Writing an entry for “Interactible”/)).toBeInTheDocument();
    expect(screen.getByTitle("Writing a new entry right now.")).toHaveTextContent("Generating");
  });
});
