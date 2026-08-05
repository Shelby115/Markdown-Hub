import { render } from "@testing-library/react";
import { describe, expect, it } from "vitest";
import { renderSearchSnippet } from "./searchSnippet";

const MALICIOUS_SNIPPET = '<img src=x onerror="window.__pwned = true"> <mark>hit</mark>';

/** Reproduces the old SearchBar.tsx implementation this fix replaced, so the "before" behavior
 * is provable in a test rather than just asserted in a commit message. */
function OldVulnerableSnippet({ snippet }: { snippet: string }) {
  return <div dangerouslySetInnerHTML={{ __html: snippet }} />;
}

describe("the dangerouslySetInnerHTML approach this replaces (proves the bug existed)", () => {
  it("turns attacker-controlled snippet text into a real, executable <img> element", () => {
    const { container } = render(<OldVulnerableSnippet snippet={MALICIOUS_SNIPPET} />);
    expect(container.querySelector("img")).not.toBeNull();
  });
});

describe("renderSearchSnippet", () => {
  it("highlights <mark>-wrapped segments", () => {
    const { container } = render(<div>{renderSearchSnippet("before <mark>match</mark> after")}</div>);
    expect(container.querySelector("mark")?.textContent).toBe("match");
    expect(container.textContent).toBe("before match after");
  });

  it("never turns attacker-controlled snippet text into real HTML elements", () => {
    const { container } = render(<div>{renderSearchSnippet(MALICIOUS_SNIPPET)}</div>);

    expect(container.querySelector("img")).toBeNull();
    expect(container.textContent).toContain('<img src=x onerror="window.__pwned = true">');
    expect(container.querySelector("mark")?.textContent).toBe("hit");
  });

  it("handles a snippet with no highlight at all", () => {
    const { container } = render(<div>{renderSearchSnippet("plain text, no match markers")}</div>);
    expect(container.querySelector("mark")).toBeNull();
    expect(container.textContent).toBe("plain text, no match markers");
  });

  it("handles multiple highlighted segments", () => {
    const { container } = render(<div>{renderSearchSnippet("<mark>one</mark> and <mark>two</mark>")}</div>);
    const marks = container.querySelectorAll("mark");
    expect(marks).toHaveLength(2);
    expect(marks[0].textContent).toBe("one");
    expect(marks[1].textContent).toBe("two");
  });
});
