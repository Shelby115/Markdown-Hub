import { describe, expect, it } from "vitest";
import { DICE_NOTATION_RE, parseDiceNotation, rollDiceNotation } from "./diceRoller";

function findAll(text: string): string[] {
  DICE_NOTATION_RE.lastIndex = 0;
  return Array.from(text.matchAll(DICE_NOTATION_RE)).map((m) => m[0]);
}

describe("DICE_NOTATION_RE + parseDiceNotation used together (liveMarkdown.ts's actual pattern)", () => {
  // Regression test for a real crash: liveMarkdown.ts's findDiceMatches scans a line with
  // `while ((match = DICE_NOTATION_RE.exec(line.text)))` and calls parseDiceNotation(match[0])
  // inside that loop. parseDiceNotation used to reuse the same global DICE_NOTATION_RE object
  // and reset its lastIndex to 0 on every call, corrupting the outer loop's scan position so it
  // re-found the same match forever - an infinite loop that crashed the editor (out-of-memory)
  // the moment valid dice notation was typed. A hard iteration cap here means this test fails
  // fast with a clear assertion instead of hanging if that ever regresses.
  it("scanning a line and parsing each match terminates and finds exactly the expected matches", () => {
    const text = "Roll 2d10 for damage, then 1d20+5 to hit.";
    DICE_NOTATION_RE.lastIndex = 0;
    const found: string[] = [];
    let match: RegExpExecArray | null;
    let iterations = 0;
    while ((match = DICE_NOTATION_RE.exec(text))) {
      if (++iterations > 100) throw new Error("Infinite loop detected in dice notation scanning");
      if (parseDiceNotation(match[0])) found.push(match[0]);
    }
    expect(found).toEqual(["2d10", "1d20+5"]);
  });

  it("typing a valid roll character-by-character never corrupts the shared scan regex", () => {
    // Simulates the exact trigger: the editor re-scans the whole (growing) line on every
    // keystroke as the user types "2d10" one character at a time.
    for (const partial of ["2", "2d", "2d1", "2d10"]) {
      DICE_NOTATION_RE.lastIndex = 0;
      let match: RegExpExecArray | null;
      let iterations = 0;
      while ((match = DICE_NOTATION_RE.exec(partial))) {
        if (++iterations > 100) throw new Error(`Infinite loop scanning "${partial}"`);
        parseDiceNotation(match[0]);
      }
    }
  });
});

describe("DICE_NOTATION_RE matching", () => {
  it("matches every example from the spec", () => {
    const examples = ["d20", "2d20", "2d20+1", "4d6-2", "+d20", "+1d20+5", "-d20", "-2d20+3"];
    for (const example of examples) {
      expect(findAll(example)).toEqual([example]);
    }
  });

  it("does not match inside a longer word or number", () => {
    expect(findAll("x2d20y")).toEqual([]);
    expect(findAll("12d20")).toEqual(["12d20"]); // the whole number is the dice count, not a false split
    expect(findAll("word2d20")).toEqual([]);
  });

  it("does not treat arbitrary math as dice notation", () => {
    expect(findAll("2+2")).toEqual([]);
    expect(findAll("d")).toEqual([]);
    expect(findAll("2 * 3")).toEqual([]);
  });

  it("finds multiple rolls in the same text", () => {
    expect(findAll("Roll 1d20 then 2d6+1 for damage")).toEqual(["1d20", "2d6+1"]);
  });
});

describe("parseDiceNotation", () => {
  it("defaults dice count to 1 when omitted", () => {
    expect(parseDiceNotation("d20")).toMatchObject({ diceCount: 1, sides: 20, modifier: 0, advantageMode: "normal" });
  });

  it("parses count, sides, and a positive/negative modifier", () => {
    expect(parseDiceNotation("2d20+1")).toMatchObject({ diceCount: 2, sides: 20, modifier: 1 });
    expect(parseDiceNotation("4d6-2")).toMatchObject({ diceCount: 4, sides: 6, modifier: -2 });
  });

  it("parses advantage/disadvantage prefixes, applying to the roll not the modifier", () => {
    expect(parseDiceNotation("+d20")).toMatchObject({ diceCount: 1, advantageMode: "advantage" });
    expect(parseDiceNotation("+1d20+5")).toMatchObject({ diceCount: 1, modifier: 5, advantageMode: "advantage" });
    expect(parseDiceNotation("-2d20+3")).toMatchObject({ diceCount: 2, modifier: 3, advantageMode: "disadvantage" });
  });

  it("rejects dice counts and sides outside sensible limits", () => {
    expect(parseDiceNotation("0d20")).toBeNull();
    expect(parseDiceNotation("101d20")).toBeNull();
    expect(parseDiceNotation("1d1")).toBeNull();
    expect(parseDiceNotation("1d1001")).toBeNull();
  });

  it("accepts the boundary values", () => {
    expect(parseDiceNotation("100d1000")).not.toBeNull();
    expect(parseDiceNotation("1d2")).not.toBeNull();
  });
});

describe("rollDiceNotation", () => {
  it("rolls the right number of dice, each within [1, sides]", () => {
    const parsed = parseDiceNotation("5d6")!;
    const outcome = rollDiceNotation(parsed);
    expect(outcome.primary.rolls).toHaveLength(5);
    for (const r of outcome.primary.rolls) {
      expect(r).toBeGreaterThanOrEqual(1);
      expect(r).toBeLessThanOrEqual(6);
    }
    expect(outcome.selected).toBe("primary");
    expect(outcome.alternate).toBeUndefined();
  });

  it("total includes the modifier", () => {
    const parsed = parseDiceNotation("3d6+7")!;
    const outcome = rollDiceNotation(parsed);
    expect(outcome.total).toBe(outcome.primary.rolls.reduce((a, b) => a + b, 0) + 7);
  });

  it("advantage keeps the higher of two independent rolls (including modifier)", () => {
    const parsed = parseDiceNotation("+3d6")!;
    const outcome = rollDiceNotation(parsed);
    expect(outcome.alternate).toBeDefined();
    expect(outcome.total).toBe(Math.max(outcome.primary.total, outcome.alternate!.total));
  });

  it("disadvantage keeps the lower of two independent rolls (including modifier)", () => {
    const parsed = parseDiceNotation("-3d6")!;
    const outcome = rollDiceNotation(parsed);
    expect(outcome.alternate).toBeDefined();
    expect(outcome.total).toBe(Math.min(outcome.primary.total, outcome.alternate!.total));
  });

  it("produces a roughly uniform distribution over many rolls (no modulo bias)", () => {
    const parsed = parseDiceNotation("1d6")!;
    const counts = new Array(6).fill(0);
    const trials = 6000;
    for (let i = 0; i < trials; i++) {
      counts[rollDiceNotation(parsed).primary.rolls[0] - 1]++;
    }
    // Each face should land near trials/6 (~1000); a generous tolerance keeps this from
    // being flaky while still catching a badly broken (e.g. always-1) implementation.
    for (const count of counts) {
      expect(count).toBeGreaterThan(trials / 6 - 300);
      expect(count).toBeLessThan(trials / 6 + 300);
    }
  });
});
