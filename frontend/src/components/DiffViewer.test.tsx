import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { describe, expect, it, vi } from "vitest";
import { DiffViewer } from "./DiffViewer";

describe("DiffViewer", () => {
  it("shows added/removed line counts and the older/newer labels", () => {
    render(
      <DiffViewer
        oldContent={"line one\nline two"}
        newContent={"line one\nline three"}
        oldLabel="Aug 1, 9:00 AM · Alice"
        newLabel="Aug 2, 9:00 AM · Alice"
      />
    );

    expect(screen.getByText("Aug 1, 9:00 AM · Alice")).toBeInTheDocument();
    expect(screen.getByText("Aug 2, 9:00 AM · Alice")).toBeInTheDocument();
    expect(screen.getByText("+1")).toBeInTheDocument();
    expect(screen.getByText("-1")).toBeInTheDocument();
    expect(screen.getByText("line two")).toBeInTheDocument();
    expect(screen.getByText("line three")).toBeInTheDocument();
  });

  it("shows 'No changes' when the two contents are identical", () => {
    render(<DiffViewer oldContent="same" newContent="same" oldLabel="A" newLabel="B" />);

    expect(screen.getByText("No changes")).toBeInTheDocument();
  });

  it("collapses a long unchanged run and expands it on click", async () => {
    const oldLines = Array.from({ length: 20 }, (_, i) => `line ${i}`);
    const newLines = [...oldLines];
    newLines[10] = "CHANGED LINE";
    const user = userEvent.setup();

    render(
      <DiffViewer oldContent={oldLines.join("\n")} newContent={newLines.join("\n")} oldLabel="Old" newLabel="New" />
    );

    const collapsedMarkers = screen.getAllByText(/unchanged lines?.*click to expand/);
    expect(collapsedMarkers.length).toBeGreaterThan(0);
    expect(screen.queryAllByText("line 0")).toHaveLength(0);

    await user.click(collapsedMarkers[0]);

    // Appears twice - once per side of the unchanged row.
    expect(screen.getAllByText("line 0").length).toBeGreaterThan(0);
  });

  it("renders a header action button when provided, and calls it on click", async () => {
    const onClick = vi.fn();
    const user = userEvent.setup();

    render(
      <DiffViewer
        oldContent="a"
        newContent="b"
        oldLabel="Old"
        newLabel="New"
        headerAction={{ label: "Restore this version", onClick }}
      />
    );

    await user.click(screen.getByRole("button", { name: "Restore this version" }));

    expect(onClick).toHaveBeenCalled();
  });
});
