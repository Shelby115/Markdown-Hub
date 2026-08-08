import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AiLab } from "./AiLab";

const mocks = vi.hoisted(() => ({
  getAiSettings: vi.fn(),
  listAiModels: vi.fn(),
  setAiModel: vi.fn(),
  getPools: vi.fn(),
  getPoolSettings: vi.fn(),
  getPoolEntries: vi.fn(),
  setPoolSettings: vi.fn(),
  createPool: vi.fn(),
  updatePool: vi.fn(),
  deletePool: vi.fn(),
  generatePoolEntry: vi.fn(),
  forgetEntry: vi.fn(),
}));

vi.mock("../api/client", async () => {
  const actual = await vi.importActual<typeof import("../api/client")>("../api/client");
  return {
    ...actual,
    api: {
      adminGetAiSettings: mocks.getAiSettings,
      adminListAiModels: mocks.listAiModels,
      adminSetAiModel: mocks.setAiModel,
      adminGetPools: mocks.getPools,
      adminGetPoolSettings: mocks.getPoolSettings,
      adminGetPoolEntries: mocks.getPoolEntries,
      adminSetPoolSettings: mocks.setPoolSettings,
      adminCreatePool: mocks.createPool,
      adminUpdatePool: mocks.updatePool,
      adminDeletePool: mocks.deletePool,
      adminGeneratePoolEntry: mocks.generatePoolEntry,
      aiPoolForgetEntry: mocks.forgetEntry,
    },
  };
});

const poolStatus = {
  settings: {
    paused: false,
    windowStartUtc: null,
    windowEndUtc: null,
    intervalSeconds: 60,
    usedEntryRetentionDays: 90,
  },
  runningNow: true,
  reason: "No window set, so pools are topped up at any hour.",
  generatingPoolName: null,
  secondsUntilNextCheck: 30,
  nowUtc: "12:00",
};

const pool = {
  id: 1,
  name: "Interactible",
  instructions: "- One brief interactible.",
  targetCount: 20,
  enabled: true,
  readyCount: 6,
  status: "Queued",
  statusReason: "14 more to generate - waiting for the generator's next pass.",
  updatedAtUtc: "2026-08-08T10:00:00Z",
};

function renderPage() {
  return render(
    <MemoryRouter>
      <AiLab />
    </MemoryRouter>
  );
}

beforeEach(() => {
  mocks.getAiSettings.mockReset();
  mocks.listAiModels.mockReset();
  mocks.setAiModel.mockReset();
  mocks.getPools.mockReset().mockResolvedValue([pool]);
  mocks.getPoolSettings.mockReset().mockResolvedValue(poolStatus);
  mocks.getPoolEntries.mockReset().mockResolvedValue([]);
  mocks.setPoolSettings.mockReset();
  mocks.forgetEntry.mockReset();
});

describe("AI page - model", () => {
  it("shows the configured default when no override is set, and disables Reset", async () => {
    mocks.getAiSettings.mockResolvedValue({
      selectedModel: null,
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "gpt-oss:20b",
    });
    mocks.listAiModels.mockResolvedValue({ models: ["gpt-oss:20b", "llama3.1:8b"] });

    renderPage();

    expect(await screen.findByText("gpt-oss:20b", { selector: "strong" })).toBeInTheDocument();
    expect(screen.getByText(/configured default/i)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reset to default" })).toBeDisabled();
  });

  it("saving a typed model name calls the API and updates the displayed effective model", async () => {
    mocks.getAiSettings.mockResolvedValue({
      selectedModel: null,
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "gpt-oss:20b",
    });
    mocks.listAiModels.mockResolvedValue({ models: ["gpt-oss:20b", "llama3.1:8b"] });
    mocks.setAiModel.mockResolvedValue({
      selectedModel: "llama3.1:8b",
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "llama3.1:8b",
    });

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("gpt-oss:20b", { selector: "strong" });

    const input = screen.getByLabelText("Ollama model");
    await user.clear(input);
    await user.type(input, "llama3.1:8b");
    await user.click(screen.getByRole("button", { name: "Save" }));

    await waitFor(() => expect(mocks.setAiModel).toHaveBeenCalledWith("llama3.1:8b"));
    expect(await screen.findByText("llama3.1:8b", { selector: "strong" })).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Reset to default" })).toBeEnabled();
  });

  it("Reset to default clears the override", async () => {
    mocks.getAiSettings.mockResolvedValue({
      selectedModel: "llama3.1:8b",
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "llama3.1:8b",
    });
    mocks.listAiModels.mockResolvedValue({ models: [] });
    mocks.setAiModel.mockResolvedValue({
      selectedModel: null,
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "gpt-oss:20b",
    });

    const user = userEvent.setup();
    renderPage();
    await screen.findByText("llama3.1:8b", { selector: "strong" });

    await user.click(screen.getByRole("button", { name: "Reset to default" }));

    await waitFor(() => expect(mocks.setAiModel).toHaveBeenCalledWith(null));
    expect(await screen.findByText("gpt-oss:20b", { selector: "strong" })).toBeInTheDocument();
  });

  it("shows a warning but still lets you type a model manually when listing models fails", async () => {
    mocks.getAiSettings.mockResolvedValue({
      selectedModel: null,
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "gpt-oss:20b",
    });
    mocks.listAiModels.mockRejectedValue(new Error(JSON.stringify({ message: "Couldn't reach the AI service." })));

    renderPage();

    expect(await screen.findByText(/Couldn't reach the AI service\./)).toBeInTheDocument();
    expect(screen.getByLabelText("Ollama model")).toBeEnabled();
  });
});

describe("AI page - pools", () => {
  beforeEach(() => {
    mocks.getAiSettings.mockResolvedValue({
      selectedModel: null,
      configuredDefaultModel: "gpt-oss:20b",
      effectiveModel: "gpt-oss:20b",
    });
    mocks.listAiModels.mockResolvedValue({ models: [] });
  });

  it("loads pools alongside the model settings", async () => {
    renderPage();

    expect(await screen.findByText("Interactible")).toBeInTheDocument();
    expect(screen.getByText("6/20")).toBeInTheDocument();
    expect(screen.getByTitle(/14 more to generate/)).toHaveTextContent("Queued");
  });

  it("picks up entries the background generator added, without the user doing anything", async () => {
    vi.useFakeTimers({ shouldAdvanceTime: true });
    try {
      renderPage();
      expect(await screen.findByText("6/20")).toBeInTheDocument();

      mocks.getPools.mockResolvedValue([{ ...pool, readyCount: 7 }]);
      await vi.advanceTimersByTimeAsync(5000);

      await waitFor(() => expect(screen.getByText("7/20")).toBeInTheDocument());
    } finally {
      vi.useRealTimers();
    }
  });

  it("pausing sends the current settings with paused flipped", async () => {
    mocks.setPoolSettings.mockImplementation((s) => Promise.resolve({ ...poolStatus, settings: s }));
    const user = userEvent.setup();
    renderPage();

    await user.click(await screen.findByRole("button", { name: "Pause" }));

    await waitFor(() =>
      expect(mocks.setPoolSettings).toHaveBeenCalledWith({ ...poolStatus.settings, paused: true })
    );
    expect(await screen.findByRole("button", { name: "Resume" })).toBeInTheDocument();
  });
});
