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

async function renderPanel(onSave = vi.fn()) {
  render(
    <AiTemplatePanel
      templatePath="Templates/Adventure.md"
      pageName="The Abandoned Mine"
      parsed={parsed}
      variables={{}}
      onCancel={vi.fn()}
      onSave={onSave}
    />
  );
  await screen.findByText("Scene");
  return onSave;
}

const slotBox = (id: string) => screen.getByLabelText(id) as HTMLTextAreaElement;

describe("AiTemplatePanel", () => {
  beforeEach(() => {
    aiTemplateGenerateMock.mockReset().mockResolvedValue({ content: "generated", warnings: [] });
    getAiAssistantStatusMock.mockReset().mockResolvedValue({ available: true });
  });

  it("shows the Ollama-not-found message instead of the slots when AI is unavailable", async () => {
    getAiAssistantStatusMock.mockResolvedValue({ available: false });
    render(
      <AiTemplatePanel
        templatePath="Templates/Adventure.md"
        pageName="X"
        parsed={parsed}
        variables={{}}
        onCancel={vi.fn()}
        onSave={vi.fn()}
      />
    );

    expect(await screen.findByText(/Ollama installation not found/)).toBeInTheDocument();
    expect(screen.queryByText("Scene")).not.toBeInTheDocument();
  });

  it("Generate all fills every slot and sends the earlier slots along as context", async () => {
    const user = userEvent.setup();
    aiTemplateGenerateMock.mockImplementation((_path, slotId) => Promise.resolve({ content: `${slotId} text`, warnings: [] }));
    await renderPanel();

    await user.click(screen.getByText("Generate all"));

    await waitFor(() => expect(aiTemplateGenerateMock).toHaveBeenCalledTimes(2));
    expect(slotBox("Scene#1").value).toBe("Scene#1 text");
    expect(slotBox("Interactible#1").value).toBe("Interactible#1 text");
    // The second call carries the first slot's freshly generated content as context.
    expect(aiTemplateGenerateMock.mock.calls[1][3]).toEqual([
      { id: "Scene#1", content: "Scene#1 text", locked: false },
      { id: "Interactible#1", content: "", locked: false },
    ]);
  });

  it("skips a locked slot and sends it flagged as locked", async () => {
    const user = userEvent.setup();
    await renderPanel();

    await user.click(screen.getAllByLabelText("Lock")[0]);
    await user.click(screen.getByText("Generate all"));

    await waitFor(() => expect(aiTemplateGenerateMock).toHaveBeenCalledTimes(1));
    expect(aiTemplateGenerateMock.mock.calls[0][1]).toBe("Interactible#1");
    expect(aiTemplateGenerateMock.mock.calls[0][3]).toContainEqual({ id: "Scene#1", content: "", locked: true });
  });

  it("a failed reroll keeps the previous content and shows the error on that card only", async () => {
    const user = userEvent.setup();
    await renderPanel();

    aiTemplateGenerateMock.mockResolvedValueOnce({ content: "a good scene", warnings: [] });
    await user.click(screen.getAllByLabelText("Reroll")[0]);
    await waitFor(() => expect(slotBox("Scene#1").value).toBe("a good scene"));

    aiTemplateGenerateMock.mockRejectedValueOnce(new Error(JSON.stringify({ message: "Ollama is unreachable." })));
    await user.click(screen.getAllByLabelText("Reroll")[0]);

    expect(await screen.findByText("Ollama is unreachable.")).toBeInTheDocument();
    expect(slotBox("Scene#1").value).toBe("a good scene");
  });

  it("renders validation warnings returned with a result", async () => {
    const user = userEvent.setup();
    aiTemplateGenerateMock.mockResolvedValue({ content: "too long", warnings: ["Your reply is longer than the 5-word limit."] });
    await renderPanel();

    await user.click(screen.getAllByLabelText("Reroll")[0]);

    expect(await screen.findByText(/longer than the 5-word limit/)).toBeInTheDocument();
  });

  it("saving hands back the assembled document", async () => {
    const user = userEvent.setup();
    aiTemplateGenerateMock.mockImplementation((_path, slotId) => Promise.resolve({ content: `${slotId} text`, warnings: [] }));
    const onSave = await renderPanel();

    await user.click(screen.getByText("Generate all"));
    await waitFor(() => expect(slotBox("Interactible#1").value).toBe("Interactible#1 text"));
    await user.click(screen.getByText("Save as page"));

    expect(onSave).toHaveBeenCalledWith("# Adventure\n\nScene#1 text\n\nInteractible#1 text");
  });
});
