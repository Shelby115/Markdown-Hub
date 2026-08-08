import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AiTemplateParseResult } from "../api/client";
import { AiTemplatePanel } from "./AiTemplatePanel";

const { aiTemplateGenerateMock, getAiAssistantStatusMock } = vi.hoisted(() => ({
  aiTemplateGenerateMock: vi.fn(),
  getAiAssistantStatusMock: vi.fn(),
}));

vi.mock("../api/client", async () => {
  const actual = await vi.importActual<typeof import("../api/client")>("../api/client");
  return {
    ...actual,
    api: {
      aiTemplateGenerate: aiTemplateGenerateMock,
      getAiAssistantStatus: getAiAssistantStatusMock,
    },
  };
});

const parsed: AiTemplateParseResult = {
  elements: [
    { text: "# Adventure\n\n", slotId: null },
    { text: null, slotId: "Scene#1" },
    { text: "\n\n", slotId: null },
    { text: null, slotId: "Interactible#1" },
  ],
  slots: [
    { id: "Scene#1", name: "Scene", index: 1, count: 1 },
    { id: "Interactible#1", name: "Interactible", index: 1, count: 1 },
  ],
  fillInVariables: [],
};

interface PanelOverrides {
  parsed?: AiTemplateParseResult;
  pathEditable?: boolean;
}

function renderPanel(overrides: PanelOverrides = {}) {
  const onSave = vi.fn<(content: string, pagePath: string) => void>();
  render(
    <AiTemplatePanel
      templatePath="Templates/Adventure.md"
      initialPagePath="Adventures/The Abandoned Mine"
      parsed={overrides.parsed ?? parsed}
      pathEditable={overrides.pathEditable}
      onCancel={vi.fn()}
      onSave={onSave}
    />
  );
  return onSave;
}

/** Renders and waits for the automatic first generation pass to finish. */
async function renderGenerated(overrides: PanelOverrides = {}) {
  const onSave = renderPanel(overrides);
  await waitFor(() => expect(slotBox("Interactible#1").value).not.toBe(""));
  aiTemplateGenerateMock.mockClear();
  return onSave;
}

const slotBox = (id: string) => screen.getByLabelText(id) as HTMLTextAreaElement;

describe("AiTemplatePanel", () => {
  beforeEach(() => {
    aiTemplateGenerateMock.mockReset().mockImplementation((_path, slotId) =>
      Promise.resolve({ content: `${slotId} text`, warnings: [] })
    );
    getAiAssistantStatusMock.mockReset().mockResolvedValue({ available: true });
  });

  it("shows the Ollama-not-found message instead of the slots when AI is unavailable", async () => {
    getAiAssistantStatusMock.mockResolvedValue({ available: false });
    renderPanel();

    expect(await screen.findByText(/Ollama installation not found/)).toBeInTheDocument();
    expect(screen.queryByLabelText("Scene#1")).not.toBeInTheDocument();
    expect(aiTemplateGenerateMock).not.toHaveBeenCalled();
  });

  it("generates every slot automatically on open, each seeing the previous ones", async () => {
    renderPanel();

    await waitFor(() => expect(aiTemplateGenerateMock).toHaveBeenCalledTimes(2));
    expect(slotBox("Scene#1").value).toBe("Scene#1 text");
    expect(slotBox("Interactible#1").value).toBe("Interactible#1 text");
    // The second call carries the first slot's freshly generated content as context.
    expect(aiTemplateGenerateMock.mock.calls[1][3]).toEqual([
      { id: "Scene#1", content: "Scene#1 text", locked: false },
      { id: "Interactible#1", content: "", locked: false },
    ]);
  });

  it("Regenerate all skips a locked slot and sends it flagged as locked", async () => {
    const user = userEvent.setup();
    await renderGenerated();

    await user.click(screen.getAllByLabelText("Lock")[0]);
    await user.click(screen.getByText("Regenerate all"));

    await waitFor(() => expect(aiTemplateGenerateMock).toHaveBeenCalledTimes(1));
    expect(aiTemplateGenerateMock.mock.calls[0][1]).toBe("Interactible#1");
    expect(aiTemplateGenerateMock.mock.calls[0][3]).toContainEqual({
      id: "Scene#1",
      content: "Scene#1 text",
      locked: true,
    });
  });

  it("a failed reroll keeps the previous content and shows the error on that card only", async () => {
    const user = userEvent.setup();
    await renderGenerated();

    aiTemplateGenerateMock.mockRejectedValueOnce(new Error(JSON.stringify({ message: "Ollama is unreachable." })));
    await user.click(screen.getAllByLabelText("Reroll")[0]);

    expect(await screen.findByText("Ollama is unreachable.")).toBeInTheDocument();
    expect(slotBox("Scene#1").value).toBe("Scene#1 text");
    expect(slotBox("Interactible#1").value).toBe("Interactible#1 text");
  });

  it("renders validation warnings returned with a result", async () => {
    const user = userEvent.setup();
    await renderGenerated();

    aiTemplateGenerateMock.mockResolvedValueOnce({
      content: "too long",
      warnings: ["Your reply is longer than the 5-word limit."],
    });
    await user.click(screen.getAllByLabelText("Reroll")[0]);

    expect(await screen.findByText(/longer than the 5-word limit/)).toBeInTheDocument();
  });

  it("saving hands back the assembled document and the target path", async () => {
    const user = userEvent.setup();
    const onSave = await renderGenerated();

    await user.click(screen.getByText("Save as page"));

    expect(onSave).toHaveBeenCalledWith("# Adventure\n\nScene#1 text\n\nInteractible#1 text", "Adventures/The Abandoned Mine");
  });

  it("collects fill-in variables itself and substitutes them on save", async () => {
    const user = userEvent.setup();
    const withVariable: AiTemplateParseResult = {
      ...parsed,
      elements: [...parsed.elements, { text: "\n\nBy {{Author}}", slotId: null }],
      fillInVariables: ["Author"],
    };
    const onSave = await renderGenerated({ parsed: withVariable });

    await user.type(screen.getByLabelText("Author"), "Shelby");
    await user.click(screen.getByText("Save as page"));

    expect(onSave.mock.calls[0][0]).toContain("By Shelby");
  });

  it("lets the target path be edited when the panel is opened from a template page", async () => {
    const user = userEvent.setup();
    const onSave = await renderGenerated({ pathEditable: true });

    const pathInput = screen.getByLabelText("Save as");
    await user.clear(pathInput);
    await user.type(pathInput, "Adventures/Second Session");
    await user.click(screen.getByText("Save as page"));

    expect(onSave.mock.calls[0][1]).toBe("Adventures/Second Session");
  });
});
