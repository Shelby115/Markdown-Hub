import { useMemo, useState } from "react";
import { diffLines, groupIntoBlocks, toSideBySideRows } from "./diff";

/**
 * GitHub-style side-by-side Markdown diff. Reused by both the per-document version history panel
 * and the admin activity detail view, per Activity-And-History.md section 1.9 - deliberately has
 * no knowledge of versions/documents itself, just two content strings and two labels.
 */
export function DiffViewer({
  oldContent,
  newContent,
  oldLabel,
  newLabel,
  headerAction,
}: {
  oldContent: string;
  newContent: string;
  oldLabel: string;
  newLabel: string;
  headerAction?: { label: string; onClick: () => void; busy?: boolean; disabled?: boolean };
}) {
  const blocks = useMemo(
    () => groupIntoBlocks(toSideBySideRows(diffLines(oldContent, newContent))),
    [oldContent, newContent]
  );
  const [expandedBlocks, setExpandedBlocks] = useState<Set<number>>(new Set());

  const stats = useMemo(() => {
    const lines = diffLines(oldContent, newContent);
    return {
      added: lines.filter((l) => l.type === "added").length,
      removed: lines.filter((l) => l.type === "removed").length,
    };
  }, [oldContent, newContent]);

  return (
    <div className="diff-viewer">
      <div className="diff-viewer-header">
        <div className="diff-viewer-labels">
          <span className="diff-viewer-label diff-viewer-label-old" title="Older">
            {oldLabel}
          </span>
          <span className="diff-viewer-arrow">→</span>
          <span className="diff-viewer-label diff-viewer-label-new" title="Newer">
            {newLabel}
          </span>
          <span className="diff-viewer-stats">
            {stats.added > 0 && <span className="diff-stat-added">+{stats.added}</span>}
            {stats.removed > 0 && <span className="diff-stat-removed">-{stats.removed}</span>}
            {stats.added === 0 && stats.removed === 0 && <span className="muted">No changes</span>}
          </span>
        </div>
        {headerAction && (
          <button
            className="secondary"
            disabled={headerAction.busy || headerAction.disabled}
            onClick={headerAction.onClick}
          >
            {headerAction.label}
          </button>
        )}
      </div>

      <div className="diff-viewer-table-wrap">
        <table className="diff-viewer-table">
          <tbody>
            {blocks.map((block, blockIndex) =>
              block.type === "collapsed" && !expandedBlocks.has(blockIndex) ? (
                <tr key={blockIndex} className="diff-collapsed-row">
                  <td
                    colSpan={4}
                    onClick={() => setExpandedBlocks((prev) => new Set(prev).add(blockIndex))}
                  >
                    ⋯ {block.rows.length} unchanged line{block.rows.length === 1 ? "" : "s"} - click to expand ⋯
                  </td>
                </tr>
              ) : (
                block.rows.map((row, rowIndex) => (
                  <tr key={`${blockIndex}-${rowIndex}`}>
                    <td className="diff-line-number">{row.left?.oldLineNumber ?? ""}</td>
                    <td className={`diff-line-content diff-line-${row.left?.type ?? "empty"}`}>
                      {row.left ? row.left.text || " " : ""}
                    </td>
                    <td className="diff-line-number">{row.right?.newLineNumber ?? ""}</td>
                    <td className={`diff-line-content diff-line-${row.right?.type ?? "empty"}`}>
                      {row.right ? row.right.text || " " : ""}
                    </td>
                  </tr>
                ))
              )
            )}
          </tbody>
        </table>
      </div>
    </div>
  );
}
