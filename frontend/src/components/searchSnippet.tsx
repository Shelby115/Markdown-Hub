import { Fragment, ReactNode } from "react";

/**
 * Splits a search snippet from the backend's SQLite FTS5 `snippet()` call into plain-text and
 * <mark>-highlighted segments, rendered as JSX text nodes rather than injected via
 * dangerouslySetInnerHTML. The backend only ever wraps matches in literal "<mark>"/"</mark>"
 * strings (see SearchIndexService.SearchAsync) - everything else in the snippet is raw indexed
 * page content, which is untrusted and must never be parsed as HTML. Splitting on those two
 * fixed delimiters and letting React render each segment as plain text (JSX text interpolation
 * always escapes) gets the highlighting without ever treating page content as markup.
 */
export function renderSearchSnippet(snippet: string): ReactNode {
  const parts = snippet.split(/(<mark>|<\/mark>)/);
  let marking = false;

  return parts.map((part, i) => {
    if (part === "<mark>") {
      marking = true;
      return null;
    }
    if (part === "</mark>") {
      marking = false;
      return null;
    }
    if (!part) return null;
    return marking ? <mark key={i}>{part}</mark> : <Fragment key={i}>{part}</Fragment>;
  });
}
