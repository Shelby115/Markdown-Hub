import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { VersionHistoryPanel } from "./VersionHistoryPanel";

const { getVersionHistoryMock, getVersionMock, restoreVersionMock, compareVersionsMock } = vi.hoisted(() => ({
  getVersionHistoryMock: vi.fn(),
  getVersionMock: vi.fn(),
  restoreVersionMock: vi.fn(),
  compareVersionsMock: vi.fn(),
}));

vi.mock("../api/client", async () => {
  const actual = await vi.importActual<typeof import("../api/client")>("../api/client");
  return {
    ...actual,
    api: {
      getVersionHistory: getVersionHistoryMock,
      getVersion: getVersionMock,
      restoreVersion: restoreVersionMock,
      compareVersions: compareVersionsMock,
    },
  };
});

const V2 = {
  id: 2,
  documentId: 1,
  createdAtUtc: new Date().toISOString(),
  updatedAtUtc: new Date().toISOString(),
  isOpen: true,
  versionType: "Edit",
  userId: 5,
  username: "Alice",
  relativePath: "Notes.md",
};
const V1 = { ...V2, id: 1, versionType: "Edit", createdAtUtc: new Date(Date.now() - 86_400_000).toISOString() };

describe("VersionHistoryPanel", () => {
  beforeEach(() => {
    getVersionHistoryMock.mockReset();
    getVersionMock.mockReset();
    restoreVersionMock.mockReset();
    compareVersionsMock.mockReset();
    vi.spyOn(window, "confirm").mockReturnValue(true);
  });

  it("shows a message when there is no history yet", async () => {
    getVersionHistoryMock.mockResolvedValue({ documentId: 1, relativePath: "Notes.md", isDeleted: false, versions: [] });

    render(<VersionHistoryPanel relativePath="Notes.md" onClose={vi.fn()} onRestored={vi.fn()} />);

    expect(await screen.findByText(/No history yet/)).toBeInTheDocument();
  });

  it("lists versions with author and marks the newest as Current", async () => {
    getVersionHistoryMock.mockResolvedValue({ documentId: 1, relativePath: "Notes.md", isDeleted: false, versions: [V2, V1] });

    render(<VersionHistoryPanel relativePath="Notes.md" onClose={vi.fn()} onRestored={vi.fn()} />);

    expect(await screen.findAllByText("Alice")).toHaveLength(2);
    expect(screen.getByText("Current")).toBeInTheDocument();
  });

  it("View opens a diff comparing the version against its predecessor", async () => {
    getVersionHistoryMock.mockResolvedValue({ documentId: 1, relativePath: "Notes.md", isDeleted: false, versions: [V2, V1] });
    getVersionMock.mockImplementation((id: number) =>
      Promise.resolve({ ...(id === 1 ? V1 : V2), content: id === 1 ? "Old content" : "New content" })
    );
    const user = userEvent.setup();
    render(<VersionHistoryPanel relativePath="Notes.md" onClose={vi.fn()} onRestored={vi.fn()} />);
    await screen.findAllByText("Alice");

    const viewButtons = screen.getAllByRole("button", { name: "Compare with Previous" });
    await user.click(viewButtons[0]); // the newest version

    expect(await screen.findByText("Old content")).toBeInTheDocument();
    expect(screen.getByText("New content")).toBeInTheDocument();
  });

  it("Restore calls the API, notifies the parent, and refreshes the list", async () => {
    getVersionHistoryMock.mockResolvedValueOnce({ documentId: 1, relativePath: "Notes.md", isDeleted: false, versions: [V2, V1] });
    getVersionHistoryMock.mockResolvedValueOnce({ documentId: 1, relativePath: "Notes.md", isDeleted: false, versions: [{ ...V2, id: 3 }, V2, V1] });
    restoreVersionMock.mockResolvedValue({ ...V1, id: 3 });
    const onRestored = vi.fn();
    const user = userEvent.setup();
    render(<VersionHistoryPanel relativePath="Notes.md" onClose={vi.fn()} onRestored={onRestored} />);
    await screen.findAllByText("Alice");

    const restoreButtons = screen.getAllByRole("button", { name: "Restore" });
    // The newest ("Current") row's Restore button is disabled; restore an older one.
    await user.click(restoreButtons[1]);

    await waitFor(() => expect(restoreVersionMock).toHaveBeenCalledWith(V1.id));
    expect(onRestored).toHaveBeenCalled();
    expect(getVersionHistoryMock).toHaveBeenCalledTimes(2);
  });
});
