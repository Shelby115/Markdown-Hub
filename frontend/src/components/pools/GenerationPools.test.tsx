import { act, render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { afterEach, describe, expect, it, vi } from "vitest";
import { GenerationPools } from "./GenerationPools";
import { GenerationPoolsController } from "./useGenerationPools";

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

const settings = {
  paused: false,
  windowStartUtc: "22:00",
  windowEndUtc: "06:00",
  intervalSeconds: 60,
  usedEntryRetentionDays: 90,
};

function controller(overrides: Partial<GenerationPoolsController> = {}): GenerationPoolsController {
  return {
    status: {
      settings,
      runningNow: true,
      reason: "Inside the allowed window (22:00-06:00 UTC), topping up whichever pool needs it most.",
      generatingPoolName: null,
      secondsUntilNextCheck: 45,
      nowUtc: "23:30",
    },
    settingsForm: settings,
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

afterEach(() => vi.useRealTimers());

describe("GenerationPools", () => {
  it("shows every pool's status in the rail, without needing it selected first", () => {
    render(<GenerationPools c={controller()} />);

    expect(screen.getByTitle(/14 more to generate/)).toHaveTextContent("Waiting");
    expect(screen.getByTitle("All 50 entries are ready.")).toHaveTextContent("Full");
  });

  it("says why the generator is or isn't running", () => {
    render(<GenerationPools c={controller()} />);

    expect(screen.getByText(/Inside the allowed window/)).toBeInTheDocument();
  });

  it("counts down to the generator's next pass instead of quoting the interval", () => {
    render(<GenerationPools c={controller()} />);

    expect(screen.getByRole("timer")).toHaveAccessibleName("Next check in 45 seconds");
    expect(screen.queryByText(/checking every/i)).not.toBeInTheDocument();
  });

  it("ticks the countdown down between server updates", () => {
    vi.useFakeTimers();
    render(<GenerationPools c={controller()} />);

    act(() => void vi.advanceTimersByTime(3000));

    expect(screen.getByRole("timer")).toHaveAccessibleName("Next check in 42 seconds");
  });

  it("replaces the countdown with a working indicator while an entry is being written", () => {
    const c = controller();
    render(
      <GenerationPools
        c={{ ...c, status: { ...c.status!, generatingPoolName: "Interactible" } }}
      />
    );

    expect(screen.queryByRole("timer")).not.toBeInTheDocument();
    expect(screen.getByTitle("Writing an entry now.")).toBeInTheDocument();
    expect(screen.getByText(/Writing an entry for “Interactible”/)).toBeInTheDocument();
  });

  it("hides the countdown while paused - there is no pass to wait for", () => {
    const c = controller();
    render(
      <GenerationPools
        c={{ ...c, status: { ...c.status!, runningNow: false, settings: { ...settings, paused: true } } }}
      />
    );

    expect(screen.queryByRole("timer")).not.toBeInTheDocument();
    expect(screen.getByText("Paused")).toBeInTheDocument();
  });

  it("selecting a pool loads its prompt and ready entries", async () => {
    const user = userEvent.setup();
    const c = controller();
    render(<GenerationPools c={c} />);

    await user.click(screen.getByText("Interactible"));

    expect(c.selectPool).toHaveBeenCalledWith(pools[0]);
  });

  it("shows the prompt and forgettable entries once a pool is selected", () => {
    render(<GenerationPools c={controller({ selected: 1 })} />);

    expect(screen.getByLabelText("Pool prompt")).toHaveValue("- One brief interactible.");
    const entry = screen.getByText("A rusted lantern.");
    expect(within(entry.parentElement!).getByRole("button", { name: "Forget" })).toBeInTheDocument();
  });

  it("forgetting an entry goes through the controller", async () => {
    const user = userEvent.setup();
    const c = controller({ selected: 1 });
    render(<GenerationPools c={c} />);

    await user.click(screen.getByRole("button", { name: "Forget" }));

    expect(c.forget).toHaveBeenCalledWith(9);
  });

  it("pausing goes through the controller and flips the button", async () => {
    const user = userEvent.setup();
    const c = controller();
    render(<GenerationPools c={c} />);

    await user.click(screen.getByRole("button", { name: "Pause" }));

    await waitFor(() => expect(c.togglePause).toHaveBeenCalled());
  });

  it("a new pool asks for a name; an existing one doesn't", () => {
    const { rerender } = render(<GenerationPools c={controller({ selected: "new" })} />);
    expect(screen.getByLabelText("Name")).toBeInTheDocument();

    rerender(<GenerationPools c={controller({ selected: 1 })} />);
    expect(screen.queryByLabelText("Name")).not.toBeInTheDocument();
  });
});
