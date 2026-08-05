import { render, screen, waitFor, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { AiAssistantPanel } from "./AiAssistantPanel";

const { aiAssistantMock, suggestWikiLinksMock, getPageMock, savePageMock, getAiAssistantStatusMock } = vi.hoisted(() => ({
  aiAssistantMock: vi.fn(),
  suggestWikiLinksMock: vi.fn(),
  getPageMock: vi.fn(),
  savePageMock: vi.fn(),
  getAiAssistantStatusMock: vi.fn(),
}));

vi.mock("../api/client", async () => {
  const actual = await vi.importActual<typeof import("../api/client")>("../api/client");
  return {
    ...actual,
    api: {
      aiAssistant: aiAssistantMock,
      suggestWikiLinks: suggestWikiLinksMock,
      getPage: getPageMock,
      savePage: savePageMock,
      getAiAssistantStatus: getAiAssistantStatusMock,
    },
  };
});

/** Renders expanded and waits out the on-mount availability check, landing on the full panel -
 * the shape every test that exercises actual panel content needs. */
async function renderAvailable(props: Partial<Parameters<typeof AiAssistantPanel>[0]> = {}) {
  render(
    <AiAssistantPanel
      currentPagePath={props.currentPagePath ?? null}
      collapsed={false}
      onCollapsedChange={props.onCollapsedChange ?? vi.fn()}
      onContentAddedToCurrentPage={props.onContentAddedToCurrentPage ?? vi.fn()}
    />
  );
  await screen.findByText("Context");
}

describe("AiAssistantPanel", () => {
  beforeEach(() => {
    aiAssistantMock.mockReset();
    suggestWikiLinksMock.mockReset();
    getPageMock.mockReset();
    savePageMock.mockReset();
    getAiAssistantStatusMock.mockReset().mockResolvedValue({ available: true });
  });

  it("renders just a small expand button instead of the full panel when collapsed", () => {
    render(<AiAssistantPanel currentPagePath="Gandalf.md" collapsed={true} onCollapsedChange={vi.fn()} onContentAddedToCurrentPage={vi.fn()} />);
    expect(screen.queryByText("Context")).not.toBeInTheDocument(); // full-panel-only section
    expect(screen.getByTitle("Show AI Assistant")).toBeInTheDocument();
  });

  it("expanding via the button seeds context with the current page", async () => {
    const onCollapsedChange = vi.fn();
    const user = userEvent.setup();
    render(<AiAssistantPanel currentPagePath="Gandalf.md" collapsed={true} onCollapsedChange={onCollapsedChange} onContentAddedToCurrentPage={vi.fn()} />);

    await user.click(screen.getByTitle("Show AI Assistant"));
    expect(onCollapsedChange).toHaveBeenCalledWith(false);
  });

  it("shows a checking state before the availability check resolves", () => {
    getAiAssistantStatusMock.mockReturnValue(new Promise(() => {})); // never resolves
    render(<AiAssistantPanel currentPagePath={null} collapsed={false} onCollapsedChange={vi.fn()} onContentAddedToCurrentPage={vi.fn()} />);

    expect(screen.getByText("Checking AI availability…")).toBeInTheDocument();
    expect(screen.queryByText("Context")).not.toBeInTheDocument();
  });

  it("shows an install prompt instead of the panel when Ollama isn't available", async () => {
    getAiAssistantStatusMock.mockResolvedValue({ available: false });
    render(<AiAssistantPanel currentPagePath={null} collapsed={false} onCollapsedChange={vi.fn()} onContentAddedToCurrentPage={vi.fn()} />);

    expect(await screen.findByText(/Ollama installation not found/)).toBeInTheDocument();
    expect(screen.getByText("OLLAMA_BASE_URL")).toBeInTheDocument();
    expect(screen.getByText("OLLAMA_MODEL")).toBeInTheDocument();
    expect(screen.queryByText("Context")).not.toBeInTheDocument();
  });

  it("shows the install prompt if the status check itself fails", async () => {
    getAiAssistantStatusMock.mockRejectedValue(new Error("network error"));
    render(<AiAssistantPanel currentPagePath={null} collapsed={false} onCollapsedChange={vi.fn()} onContentAddedToCurrentPage={vi.fn()} />);

    expect(await screen.findByText(/Ollama installation not found/)).toBeInTheDocument();
  });

  it("shows no context pages when opened with none active", async () => {
    await renderAvailable();
    expect(screen.getByText("No pages selected yet.")).toBeInTheDocument();
  });

  it("adding a page via the picker adds it to context and removing it takes it back out", async () => {
    suggestWikiLinksMock.mockResolvedValue([{ relativePath: "Aragorn.md", pageName: "Aragorn" }]);
    const user = userEvent.setup();
    await renderAvailable();

    await user.type(screen.getByPlaceholderText("Add a page as context…"), "Arag");
    await waitFor(() => expect(screen.getByText("Aragorn")).toBeInTheDocument());
    await user.click(screen.getByText("Aragorn"));

    expect(screen.getByText("Aragorn.md")).toBeInTheDocument();

    const contextItem = screen.getByText("Aragorn.md").closest("li")!;
    await user.click(within(contextItem).getByRole("button"));
    expect(screen.queryByText("Aragorn.md")).not.toBeInTheDocument();
  });

  it("refuses to run an action with no context selected", async () => {
    const user = userEvent.setup();
    await renderAvailable();

    await user.click(screen.getByRole("button", { name: "Summarize" }));

    expect(screen.getByText("Select at least one page as context first.")).toBeInTheDocument();
    expect(aiAssistantMock).not.toHaveBeenCalled();
  });

  it("refuses to Ask with no question typed", async () => {
    const user = userEvent.setup();
    await renderAvailable({ currentPagePath: "Gandalf.md" });

    await user.click(screen.getByRole("button", { name: "Ask" }));

    expect(screen.getByText("Enter a question first.")).toBeInTheDocument();
    expect(aiAssistantMock).not.toHaveBeenCalled();
  });

  it("runs Summarize with the selected context and displays the resulting card", async () => {
    aiAssistantMock.mockResolvedValue({ results: [{ title: "Summary", content: "Gandalf is a wizard." }] });
    const user = userEvent.setup();
    await renderAvailable({ currentPagePath: "Gandalf.md" });

    await user.click(screen.getByRole("button", { name: "Summarize" }));

    expect(aiAssistantMock).toHaveBeenCalledWith("Summarize", null, ["Gandalf.md"]);
    expect(await screen.findByText("Gandalf is a wizard.")).toBeInTheDocument();
  });

  it("Delete removes a result card", async () => {
    aiAssistantMock.mockResolvedValue({ results: [{ title: "Summary", content: "Gandalf is a wizard." }] });
    const user = userEvent.setup();
    await renderAvailable({ currentPagePath: "Gandalf.md" });

    await user.click(screen.getByRole("button", { name: "Summarize" }));
    await screen.findByText("Gandalf is a wizard.");
    await user.click(screen.getByRole("button", { name: "Delete" }));

    expect(screen.queryByText("Gandalf is a wizard.")).not.toBeInTheDocument();
  });

  it("Add to Page saves the appended content and notifies the parent", async () => {
    aiAssistantMock.mockResolvedValue({ results: [{ title: "Summary", content: "New info." }] });
    getPageMock.mockResolvedValue({ content: "Existing content.", lastModifiedUtc: "2026-01-01T00:00:00Z" });
    savePageMock.mockResolvedValue({});
    const onAdded = vi.fn();
    const user = userEvent.setup();
    await renderAvailable({ currentPagePath: "Gandalf.md", onContentAddedToCurrentPage: onAdded });

    await user.click(screen.getByRole("button", { name: "Summarize" }));
    await screen.findByText("New info.");
    await user.click(screen.getByRole("button", { name: "Add to Page" }));

    await waitFor(() =>
      expect(savePageMock).toHaveBeenCalledWith("Gandalf.md", "Existing content.\n\nNew info.\n", "2026-01-01T00:00:00Z")
    );
    expect(onAdded).toHaveBeenCalled();
    expect(await screen.findByText("Added ✓")).toBeInTheDocument();
  });

  it("Add to Page is disabled when no page is currently open", async () => {
    aiAssistantMock.mockResolvedValue({ results: [{ title: "Summary", content: "New info." }] });
    suggestWikiLinksMock.mockResolvedValue([{ relativePath: "Gandalf.md", pageName: "Gandalf" }]);
    const user = userEvent.setup();
    await renderAvailable();

    await user.type(screen.getByPlaceholderText("Add a page as context…"), "Gand");
    await waitFor(() => expect(screen.getByText("Gandalf")).toBeInTheDocument());
    await user.click(screen.getByText("Gandalf"));
    await user.click(screen.getByRole("button", { name: "Summarize" }));
    await screen.findByText("New info.");

    expect(screen.getByRole("button", { name: "Add to Page" })).toBeDisabled();
  });

  it("shows an error message when the assistant request fails", async () => {
    aiAssistantMock.mockRejectedValue(new Error(JSON.stringify({ message: "Ollama is unreachable." })));
    const user = userEvent.setup();
    await renderAvailable({ currentPagePath: "Gandalf.md" });

    await user.click(screen.getByRole("button", { name: "Summarize" }));

    expect(await screen.findByText("Ollama is unreachable.")).toBeInTheDocument();
  });
});
