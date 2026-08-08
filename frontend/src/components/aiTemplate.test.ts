import { describe, expect, it } from "vitest";
import { AiTemplateElement, AiTemplateSlot } from "../api/client";
import { assembleDocument, groupSlots } from "./aiTemplate";

const elements: AiTemplateElement[] = [
  { text: "# Adventure\n\n", slotId: null },
  { text: null, slotId: "Scene#1" },
  { text: "\n\n## Interactibles\n\n", slotId: null },
  { text: null, slotId: "Interactible#1" },
  { text: "\n", slotId: null },
  { text: null, slotId: "Interactible#2" },
  { text: "\n", slotId: null },
];

describe("assembleDocument", () => {
  it("preserves the template's literal Markdown exactly and substitutes slots in order", () => {
    const result = assembleDocument(elements, {
      "Scene#1": "An abandoned mine.",
      "Interactible#1": "**Minecart**. Half buried.",
      "Interactible#2": "**Sign**. Its paint has flaked.",
    });

    expect(result).toBe(
      "# Adventure\n\nAn abandoned mine.\n\n## Interactibles\n\n**Minecart**. Half buried.\n**Sign**. Its paint has flaked.\n"
    );
  });

  it("leaves an ungenerated slot empty rather than writing its placeholder back", () => {
    const result = assembleDocument(elements, { "Scene#1": "An abandoned mine." });

    expect(result).not.toContain("{{");
    expect(result).toContain("An abandoned mine.");
  });

  it("trims stray whitespace around generated content", () => {
    const result = assembleDocument([{ text: null, slotId: "Scene#1" }], { "Scene#1": "\n  A mine.  \n" });

    expect(result).toBe("A mine.");
  });

  it("substitutes fill-in variables in the literal text", () => {
    const result = assembleDocument([{ text: "By {{Author}}", slotId: null }], {}, { Author: "Shelby" });

    expect(result).toBe("By Shelby");
  });

  it("leaves an unknown variable alone instead of blanking it", () => {
    const result = assembleDocument([{ text: "By {{Author}}", slotId: null }], {}, {});

    expect(result).toBe("By {{Author}}");
  });
});

describe("groupSlots", () => {
  it("groups by name keeping document order", () => {
    const slots: AiTemplateSlot[] = [
      { id: "Scene#1", name: "Scene", index: 1, count: 1 },
      { id: "Interactible#1", name: "Interactible", index: 1, count: 2 },
      { id: "Interactible#2", name: "Interactible", index: 2, count: 2 },
    ];

    const groups = groupSlots(slots);

    expect(groups.map((g) => g.name)).toEqual(["Scene", "Interactible"]);
    expect(groups[1].slots.map((s) => s.id)).toEqual(["Interactible#1", "Interactible#2"]);
  });
});
