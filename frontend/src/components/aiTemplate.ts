import { AiTemplateElement, AiTemplateSlot } from "../api/client";

/**
 * Rebuilds the document from the template's literal Markdown plus each slot's generated content.
 * The structure comes entirely from the template - the AI only ever supplied the slot values.
 */
export function assembleDocument(
  elements: AiTemplateElement[],
  slotContents: Record<string, string>,
  variables: Record<string, string> = {}
): string {
  return elements
    .map((element) => {
      if (element.slotId !== null) {
        return (slotContents[element.slotId] ?? "").trim();
      }
      return substituteVariables(element.text ?? "", variables);
    })
    .join("");
}

function substituteVariables(text: string, values: Record<string, string>): string {
  return text.replace(/\{\{([^}]+)\}\}/g, (whole, name) => {
    const value = values[name.trim()];
    return value === undefined ? whole : value;
  });
}

export interface SlotGroup {
  name: string;
  slots: AiTemplateSlot[];
}

/** Groups slots by placeholder name, keeping document order. */
export function groupSlots(slots: AiTemplateSlot[]): SlotGroup[] {
  const groups: SlotGroup[] = [];
  for (const slot of slots) {
    const existing = groups.find((g) => g.name === slot.name);
    if (existing) {
      existing.slots.push(slot);
    } else {
      groups.push({ name: slot.name, slots: [slot] });
    }
  }
  return groups;
}
