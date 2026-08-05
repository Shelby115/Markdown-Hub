export type DiffLineType = "unchanged" | "added" | "removed";

export interface DiffLine {
  type: DiffLineType;
  oldLineNumber: number | null;
  newLineNumber: number | null;
  text: string;
}

/**
 * Line-based diff via a classic LCS dynamic-programming table - deliberately simple rather than
 * a full Myers diff, since the documents here are small Markdown pages, not source trees. O(n*m)
 * time/space is fine at that scale and keeps this dependency-free.
 */
export function diffLines(oldText: string, newText: string): DiffLine[] {
  const oldLines = oldText.split("\n");
  const newLines = newText.split("\n");
  const n = oldLines.length;
  const m = newLines.length;

  const dp: number[][] = Array.from({ length: n + 1 }, () => new Array<number>(m + 1).fill(0));
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      dp[i][j] = oldLines[i] === newLines[j] ? dp[i + 1][j + 1] + 1 : Math.max(dp[i + 1][j], dp[i][j + 1]);
    }
  }

  const result: DiffLine[] = [];
  let i = 0;
  let j = 0;
  let oldLineNo = 1;
  let newLineNo = 1;
  while (i < n && j < m) {
    if (oldLines[i] === newLines[j]) {
      result.push({ type: "unchanged", oldLineNumber: oldLineNo++, newLineNumber: newLineNo++, text: oldLines[i] });
      i++;
      j++;
    } else if (dp[i + 1][j] >= dp[i][j + 1]) {
      result.push({ type: "removed", oldLineNumber: oldLineNo++, newLineNumber: null, text: oldLines[i] });
      i++;
    } else {
      result.push({ type: "added", oldLineNumber: null, newLineNumber: newLineNo++, text: newLines[j] });
      j++;
    }
  }
  while (i < n) result.push({ type: "removed", oldLineNumber: oldLineNo++, newLineNumber: null, text: oldLines[i++] });
  while (j < m) result.push({ type: "added", oldLineNumber: null, newLineNumber: newLineNo++, text: newLines[j++] });

  return result;
}

export interface SideBySideRow {
  left: DiffLine | null;
  right: DiffLine | null;
}

/** Pairs up a run of removed/added lines into aligned left/right rows, GitHub-split-view style -
 * removed[k] sits beside added[k]; whichever side runs out gets an empty cell. */
export function toSideBySideRows(lines: DiffLine[]): SideBySideRow[] {
  const rows: SideBySideRow[] = [];
  let i = 0;
  while (i < lines.length) {
    if (lines[i].type === "unchanged") {
      rows.push({ left: lines[i], right: lines[i] });
      i++;
      continue;
    }
    const removed: DiffLine[] = [];
    while (i < lines.length && lines[i].type === "removed") removed.push(lines[i++]);
    const added: DiffLine[] = [];
    while (i < lines.length && lines[i].type === "added") added.push(lines[i++]);

    const max = Math.max(removed.length, added.length);
    for (let k = 0; k < max; k++) {
      rows.push({ left: removed[k] ?? null, right: added[k] ?? null });
    }
  }
  return rows;
}

export interface DiffBlock {
  type: "context" | "collapsed";
  rows: SideBySideRow[];
}

const isUnchangedRow = (row: SideBySideRow) => row.left?.type === "unchanged";

/**
 * Groups side-by-side rows into renderable blocks, collapsing long interior runs of unchanged
 * lines down to a small marker (keeping `contextLines` of surrounding context visible on each
 * side of a change, GitHub-diff style) so an unrelated 500-line file doesn't bury the actual
 * edit. The caller decides how a "collapsed" block is presented/expanded.
 */
export function groupIntoBlocks(rows: SideBySideRow[], contextLines = 3): DiffBlock[] {
  const blocks: DiffBlock[] = [];
  let i = 0;
  while (i < rows.length) {
    if (!isUnchangedRow(rows[i])) {
      const start = i;
      while (i < rows.length && !isUnchangedRow(rows[i])) i++;
      blocks.push({ type: "context", rows: rows.slice(start, i) });
      continue;
    }

    const start = i;
    while (i < rows.length && isUnchangedRow(rows[i])) i++;
    const run = rows.slice(start, i);
    const isFirstBlock = start === 0;
    const isLastBlock = i === rows.length;

    if (run.length <= contextLines * 2 + 1) {
      blocks.push({ type: "context", rows: run });
      continue;
    }

    if (isFirstBlock) {
      blocks.push({ type: "collapsed", rows: run.slice(0, run.length - contextLines) });
      blocks.push({ type: "context", rows: run.slice(run.length - contextLines) });
    } else if (isLastBlock) {
      blocks.push({ type: "context", rows: run.slice(0, contextLines) });
      blocks.push({ type: "collapsed", rows: run.slice(contextLines) });
    } else {
      blocks.push({ type: "context", rows: run.slice(0, contextLines) });
      blocks.push({ type: "collapsed", rows: run.slice(contextLines, run.length - contextLines) });
      blocks.push({ type: "context", rows: run.slice(run.length - contextLines) });
    }
  }
  return blocks;
}
