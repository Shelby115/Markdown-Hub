import { EditorState } from "@codemirror/state";
import { EditorView } from "@codemirror/view";
import { afterEach, describe, expect, it, vi } from "vitest";

vi.mock("../api/client", () => ({
  api: {
    resolveAttachment: vi.fn(async (filename: string) => ({ relativePath: filename })),
    attachmentStreamUrl: vi.fn(async (relativePath: string) => `/api/attachments/${relativePath}?access_token=test`),
    fetchAttachmentBlob: vi.fn(async () => new Blob(["fake"])),
    getPage: vi.fn(async () => ({
      pageName: "song",
      html: "<p>garbled binary content should never render for a media embed</p>",
      relativePath: "song.mp3",
    })),
  },
}));

import { api } from "../api/client";
import { liveMarkdownPreview } from "./liveMarkdown";

const openViews: EditorView[] = [];

function makeView(doc: string) {
  const state = EditorState.create({
    doc,
    extensions: [liveMarkdownPreview(() => {}, "")],
  });
  const view = new EditorView({ state, parent: document.createElement("div") });
  openViews.push(view);
  return view;
}

async function flushAsyncWork(view: EditorView, attempts = 50) {
  for (let i = 0; i < attempts; i++) {
    await new Promise((r) => setTimeout(r, 5));
    // Widget loading dispatches an effect which triggers a redraw - give it a chance to settle.
    view.requestMeasure();
  }
}

describe("media embeds route to their own widget, never note-transclusion", () => {
  afterEach(() => {
    while (openViews.length) openViews.pop()?.destroy();
    vi.clearAllMocks();
  });

  it("![[song.mp3]] renders an <audio> element, and never calls api.getPage", async () => {
    const view = makeView("![[song.mp3]]");
    await flushAsyncWork(view);

    expect(view.dom.querySelector("audio")).not.toBeNull();
    expect(view.dom.querySelector(".cm-md-transclusion")).toBeNull();
    expect(api.getPage).not.toHaveBeenCalled();
  });

  it("![[clip.mp4]] renders a <video> element, and never calls api.getPage", async () => {
    const view = makeView("![[clip.mp4]]");
    await flushAsyncWork(view);

    expect(view.dom.querySelector("video")).not.toBeNull();
    expect(view.dom.querySelector(".cm-md-transclusion")).toBeNull();
    expect(api.getPage).not.toHaveBeenCalled();
  });

  it("![[Handbook.pdf]] renders an <iframe> element, and never calls api.getPage", async () => {
    const view = makeView("![[Handbook.pdf]]");
    await flushAsyncWork(view);

    expect(view.dom.querySelector("iframe")).not.toBeNull();
    expect(view.dom.querySelector(".cm-md-transclusion")).toBeNull();
    expect(api.getPage).not.toHaveBeenCalled();
  });

  it("![[Some Page]] (no extension) still goes through note-transclusion as before", async () => {
    const view = makeView("![[Some Page]]");
    await flushAsyncWork(view);

    expect(view.dom.querySelector(".cm-md-transclusion")).not.toBeNull();
    expect(api.getPage).toHaveBeenCalled();
  });
});
