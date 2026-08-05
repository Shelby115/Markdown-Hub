import { describe, expect, it } from "vitest";
import { diffLines, groupIntoBlocks, toSideBySideRows } from "./diff";

describe("diffLines", () => {
  it("identical content produces only unchanged lines", () => {
    const result = diffLines("a\nb\nc", "a\nb\nc");

    expect(result.every((l) => l.type === "unchanged")).toBe(true);
    expect(result.map((l) => l.text)).toEqual(["a", "b", "c"]);
  });

  it("detects a pure addition", () => {
    const result = diffLines("a\nb", "a\nb\nc");

    expect(result).toEqual([
      { type: "unchanged", oldLineNumber: 1, newLineNumber: 1, text: "a" },
      { type: "unchanged", oldLineNumber: 2, newLineNumber: 2, text: "b" },
      { type: "added", oldLineNumber: null, newLineNumber: 3, text: "c" },
    ]);
  });

  it("detects a pure removal", () => {
    const result = diffLines("a\nb\nc", "a\nb");

    expect(result).toEqual([
      { type: "unchanged", oldLineNumber: 1, newLineNumber: 1, text: "a" },
      { type: "unchanged", oldLineNumber: 2, newLineNumber: 2, text: "b" },
      { type: "removed", oldLineNumber: 3, newLineNumber: null, text: "c" },
    ]);
  });

  it("detects a line replaced in the middle, keeping unrelated lines unchanged", () => {
    const result = diffLines("a\nb\nc", "a\nX\nc");

    expect(result).toEqual([
      { type: "unchanged", oldLineNumber: 1, newLineNumber: 1, text: "a" },
      { type: "removed", oldLineNumber: 2, newLineNumber: null, text: "b" },
      { type: "added", oldLineNumber: null, newLineNumber: 2, text: "X" },
      { type: "unchanged", oldLineNumber: 3, newLineNumber: 3, text: "c" },
    ]);
  });

  it("empty to non-empty adds both real lines", () => {
    // "".split("\n") is [""], so the empty original line is removed and replaced by two
    // genuinely new lines - not literally zero diff lines.
    const result = diffLines("", "a\nb");

    expect(result.filter((l) => l.type === "added").map((l) => l.text)).toEqual(["a", "b"]);
    expect(result.filter((l) => l.type === "removed")).toEqual([
      { type: "removed", oldLineNumber: 1, newLineNumber: null, text: "" },
    ]);
  });

  it("both empty produces no diff lines with content", () => {
    const result = diffLines("", "");

    // "".split("\n") is [""], so a single unchanged empty line is expected, not zero lines.
    expect(result).toEqual([{ type: "unchanged", oldLineNumber: 1, newLineNumber: 1, text: "" }]);
  });
});

describe("toSideBySideRows", () => {
  it("unchanged lines appear on both sides of the same row", () => {
    const rows = toSideBySideRows(diffLines("a\nb", "a\nb"));

    expect(rows).toHaveLength(2);
    expect(rows[0].left).toBe(rows[0].right);
  });

  it("pairs a removed line with its replacement added line", () => {
    const rows = toSideBySideRows(diffLines("a\nb\nc", "a\nX\nc"));

    expect(rows).toHaveLength(3);
    expect(rows[1].left?.text).toBe("b");
    expect(rows[1].right?.text).toBe("X");
  });

  it("an unpaired addition gets a null left side", () => {
    const rows = toSideBySideRows(diffLines("a", "a\nb\nc"));

    const addedRows = rows.filter((r) => r.left === null);
    expect(addedRows.map((r) => r.right?.text)).toEqual(["b", "c"]);
  });

  it("an unpaired removal gets a null right side", () => {
    const rows = toSideBySideRows(diffLines("a\nb\nc", "a"));

    const removedRows = rows.filter((r) => r.right === null);
    expect(removedRows.map((r) => r.left?.text)).toEqual(["b", "c"]);
  });
});

describe("groupIntoBlocks", () => {
  it("a short diff with no long unchanged run stays as a single context block", () => {
    const rows = toSideBySideRows(diffLines("a\nb\nc", "a\nX\nc"));

    const blocks = groupIntoBlocks(rows, 3);

    expect(blocks.every((b) => b.type === "context")).toBe(true);
  });

  it("collapses a long interior unchanged run, keeping context lines around the change", () => {
    const oldLines = Array.from({ length: 20 }, (_, i) => `line ${i}`);
    const newLines = [...oldLines];
    newLines[10] = "CHANGED";
    const rows = toSideBySideRows(diffLines(oldLines.join("\n"), newLines.join("\n")));

    const blocks = groupIntoBlocks(rows, 3);

    const collapsed = blocks.filter((b) => b.type === "collapsed");
    expect(collapsed.length).toBeGreaterThan(0);
    // Every collapsed row must be a genuinely unchanged one, never part of the actual edit.
    for (const block of collapsed) {
      for (const row of block.rows) {
        expect(row.left?.type).toBe("unchanged");
      }
    }
  });

  it("never collapses the actual change itself", () => {
    const oldLines = Array.from({ length: 20 }, (_, i) => `line ${i}`);
    const newLines = [...oldLines];
    newLines[10] = "CHANGED";
    const rows = toSideBySideRows(diffLines(oldLines.join("\n"), newLines.join("\n")));

    const blocks = groupIntoBlocks(rows, 3);

    const contextRows = blocks.filter((b) => b.type === "context").flatMap((b) => b.rows);
    expect(contextRows.some((r) => r.right?.text === "CHANGED")).toBe(true);
  });
});
