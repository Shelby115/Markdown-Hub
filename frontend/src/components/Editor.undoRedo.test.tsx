import { redo, redoDepth, undo, undoDepth } from "@codemirror/commands";
import { EditorState } from "@codemirror/state";
import { EditorView } from "@codemirror/view";
import { basicSetup } from "@uiw/codemirror-extensions-basic-setup";
import { afterEach, describe, expect, it } from "vitest";

/**
 * Editor.tsx doesn't set history/historyKeymap to false, so CodeMirror's default basicSetup
 * (which bundles the history() extension) is what powers both the Ctrl+Z/Ctrl+Shift+Z keyboard
 * shortcuts and the toolbar Undo/Redo buttons added alongside this test. Driving the view via
 * dispatched transactions (rather than simulating real keystrokes) avoids a jsdom limitation -
 * CodeMirror's layout measurement code calls Range.getClientRects, which jsdom doesn't
 * implement - while still exercising the exact same history mechanism the component relies on.
 */
const openViews: EditorView[] = [];

function makeView(doc: string) {
  const state = EditorState.create({
    doc,
    extensions: [basicSetup({ lineNumbers: false, foldGutter: false, highlightActiveLine: false, highlightActiveLineGutter: false })],
  });
  const view = new EditorView({ state, parent: document.createElement("div") });
  openViews.push(view);
  return view;
}

describe("CodeMirror history (undo/redo) with the editor's basicSetup configuration", () => {
  afterEach(() => {
    // Undestroyed views schedule an async layout-measurement pass (requestAnimationFrame) that
    // otherwise fires after the test ends and hits APIs jsdom doesn't implement.
    while (openViews.length) openViews.pop()?.destroy();
  });

  it("has no undo/redo history for a freshly created document", () => {
    const view = makeView("Hello");
    expect(undoDepth(view.state)).toBe(0);
    expect(redoDepth(view.state)).toBe(0);
  });

  it("tracks an edit and undo/redo restores the exact prior content", () => {
    const view = makeView("Hello");

    view.dispatch({ changes: { from: view.state.doc.length, insert: " world" } });
    expect(view.state.doc.toString()).toBe("Hello world");
    expect(undoDepth(view.state)).toBeGreaterThan(0);
    expect(redoDepth(view.state)).toBe(0);

    undo(view);
    expect(view.state.doc.toString()).toBe("Hello");
    expect(redoDepth(view.state)).toBeGreaterThan(0);

    redo(view);
    expect(view.state.doc.toString()).toBe("Hello world");
  });

  it("undo is a no-op with no history, and redo is a no-op with no redo history", () => {
    const view = makeView("Hello");
    expect(undo(view)).toBe(false);

    view.dispatch({ changes: { from: 5, insert: "!" } });
    expect(redo(view)).toBe(false); // nothing has been undone yet, so there's nothing to redo
  });
});
