import { render, screen, waitFor } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { MemoryRouter } from "react-router-dom";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { ActivityLogPage } from "./ActivityLogPage";

const { adminQueryActivityMock, adminGetActivityDetailMock, adminListUsersMock, adminGetActivityActionTypesMock } = vi.hoisted(() => ({
  adminQueryActivityMock: vi.fn(),
  adminGetActivityDetailMock: vi.fn(),
  adminListUsersMock: vi.fn(),
  adminGetActivityActionTypesMock: vi.fn(),
}));

vi.mock("../api/client", async () => {
  const actual = await vi.importActual<typeof import("../api/client")>("../api/client");
  return {
    ...actual,
    api: {
      adminQueryActivity: adminQueryActivityMock,
      adminGetActivityDetail: adminGetActivityDetailMock,
      adminListUsers: adminListUsersMock,
      adminGetActivityActionTypes: adminGetActivityActionTypesMock,
    },
  };
});

function renderPage() {
  return render(
    <MemoryRouter>
      <ActivityLogPage />
    </MemoryRouter>
  );
}

const ITEM = {
  id: 1,
  timestamp: new Date().toISOString(),
  userId: 5,
  username: "Alice",
  action: "File.Modify",
  objectType: "Document",
  objectId: 10,
  targetPath: "Session 5.md",
  occurrenceCount: 1,
  lastOccurredAtUtc: null,
  relatedVersionId: 20,
  ipAddress: "203.0.113.4",
};

describe("ActivityLogPage", () => {
  beforeEach(() => {
    adminQueryActivityMock.mockReset().mockResolvedValue({ items: [ITEM], totalCount: 1, page: 1, pageSize: 50 });
    adminGetActivityDetailMock.mockReset().mockResolvedValue({ ...ITEM, details: null });
    adminListUsersMock.mockReset().mockResolvedValue([{ id: 5, username: "Alice" }]);
    adminGetActivityActionTypesMock.mockReset().mockResolvedValue(["File.Modify", "Auth.Login"]);
  });

  it("shows a concise, scannable summary for each event", async () => {
    renderPage();

    expect(await screen.findByText('Alice modified "Session 5.md"')).toBeInTheDocument();
  });

  it("shows an empty-state message when there is no activity in range", async () => {
    adminQueryActivityMock.mockResolvedValue({ items: [], totalCount: 0, page: 1, pageSize: 50 });

    renderPage();

    expect(await screen.findByText("No activity in this range.")).toBeInTheDocument();
  });

  it("expanding a row loads and shows its details, including the IP address", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText('Alice modified "Session 5.md"');

    await user.click(screen.getByText('Alice modified "Session 5.md"'));

    expect(await screen.findByText("203.0.113.4")).toBeInTheDocument();
    expect(adminGetActivityDetailMock).toHaveBeenCalledWith(1);
  });

  it("applying filters re-queries with the selected action type", async () => {
    const user = userEvent.setup();
    renderPage();
    await screen.findByText('Alice modified "Session 5.md"');
    adminQueryActivityMock.mockClear();

    await user.selectOptions(screen.getByRole("combobox", { name: "Action" }), "Auth.Login");
    await user.click(screen.getByRole("button", { name: "Apply filters" }));

    await waitFor(() =>
      expect(adminQueryActivityMock).toHaveBeenCalledWith(expect.objectContaining({ action: "Auth.Login", page: 1 }))
    );
  });

  it("shows pagination summary text", async () => {
    adminQueryActivityMock.mockResolvedValue({ items: [ITEM], totalCount: 120, page: 1, pageSize: 50 });

    renderPage();

    expect(await screen.findByText(/Page 1 of 3 \(120 total\)/)).toBeInTheDocument();
  });
});
