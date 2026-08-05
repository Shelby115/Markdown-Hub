// Dice notation: NdS, NdS+X, NdS-X, with an optional leading +/- for advantage/disadvantage
// (roll the whole NdS+X expression twice, keep the higher/lower total). N defaults to 1 when
// omitted ("d20" == "1d20"). Deliberately strict - this must never match arbitrary math, only
// this exact shape - and never matches inside a longer word/number (e.g. "2d20" inside "x2d20y").
//
// This is a stateful *global* regex (its lastIndex advances across calls to .exec()), used by
// callers that scan a whole line for multiple matches - see liveMarkdown.ts's findDiceMatches.
// parseDiceNotation below deliberately uses a *separate*, non-global regex instead of this one:
// sharing this object between the two previously caused a real bug (crashed the editor with an
// out-of-memory error the moment valid dice notation like "2d10" was typed) - parseDiceNotation
// was being called *from inside* a `while ((match = DICE_NOTATION_RE.exec(...)))` loop elsewhere,
// and it reset this same object's lastIndex back to 0 on every call, which corrupted the outer
// loop's scan position and made it re-find the same match forever.
export const DICE_NOTATION_RE = /(?<![a-zA-Z0-9])([+-])?(\d*)d(\d+)(?:([+-])(\d+))?(?![a-zA-Z0-9])/g;

// Non-global, anchored to the whole string - safe to use from anywhere, including from inside a
// loop that's iterating DICE_NOTATION_RE above, since it never touches that object's state.
const DICE_NOTATION_FULL_RE = /^([+-])?(\d*)d(\d+)(?:([+-])(\d+))?$/;

export const MAX_DICE_COUNT = 100;
export const MAX_SIDES = 1000;

export type AdvantageMode = "normal" | "advantage" | "disadvantage";

export interface ParsedDiceNotation {
  raw: string;
  diceCount: number;
  sides: number;
  modifier: number;
  advantageMode: AdvantageMode;
}

/** Parses one already-matched notation string (e.g. "+2d20+1") back into its parts, validating
 * dice-count/sides limits. Returns null if it's out of range - callers should then treat the
 * text as plain, non-interactive text rather than a roll. */
export function parseDiceNotation(raw: string): ParsedDiceNotation | null {
  const match = DICE_NOTATION_FULL_RE.exec(raw);
  if (!match) return null;

  const [, advantagePrefix, diceCountStr, sidesStr, modifierSign, modifierStr] = match;
  const diceCount = diceCountStr ? parseInt(diceCountStr, 10) : 1;
  const sides = parseInt(sidesStr, 10);
  if (diceCount < 1 || diceCount > MAX_DICE_COUNT) return null;
  if (sides < 2 || sides > MAX_SIDES) return null;

  const modifier = modifierSign ? (modifierSign === "-" ? -1 : 1) * parseInt(modifierStr, 10) : 0;
  const advantageMode: AdvantageMode = advantagePrefix === "+" ? "advantage" : advantagePrefix === "-" ? "disadvantage" : "normal";

  return { raw, diceCount, sides, modifier, advantageMode };
}

/** A single NdS+modifier roll (no advantage/disadvantage) - individual die results, in order. */
export interface DiceRollResult {
  rolls: number[];
  modifier: number;
  total: number;
}

/** Uniform random integer in [1, sides] via rejection sampling over crypto.getRandomValues, so
 * the distribution is exact (no modulo bias) and not predictable the way Math.random() is. */
function rollOneDie(sides: number): number {
  const range = sides; // outcomes 0..sides-1, then +1
  const maxUsable = Math.floor(0x100000000 / range) * range; // largest multiple of range under 2^32
  const buf = new Uint32Array(1);
  let value: number;
  do {
    crypto.getRandomValues(buf);
    value = buf[0];
  } while (value >= maxUsable);
  return (value % range) + 1;
}

function rollOnce(parsed: ParsedDiceNotation): DiceRollResult {
  const rolls = Array.from({ length: parsed.diceCount }, () => rollOneDie(parsed.sides));
  const total = rolls.reduce((sum, r) => sum + r, 0) + parsed.modifier;
  return { rolls, modifier: parsed.modifier, total };
}

export interface DiceRollOutcome {
  advantageMode: AdvantageMode;
  primary: DiceRollResult;
  /** The other roll, present only for advantage/disadvantage. */
  alternate?: DiceRollResult;
  /** Which of primary/alternate was kept as the final result. */
  selected: "primary" | "alternate";
  total: number;
}

/** Performs the actual roll(s) described by a parsed notation, applying advantage/disadvantage
 * (roll the whole thing twice, keep the higher/lower *total*) per the spec. */
export function rollDiceNotation(parsed: ParsedDiceNotation): DiceRollOutcome {
  const primary = rollOnce(parsed);
  if (parsed.advantageMode === "normal") {
    return { advantageMode: "normal", primary, selected: "primary", total: primary.total };
  }

  const alternate = rollOnce(parsed);
  const primaryWins =
    parsed.advantageMode === "advantage" ? primary.total >= alternate.total : primary.total <= alternate.total;
  return {
    advantageMode: parsed.advantageMode,
    primary,
    alternate,
    selected: primaryWins ? "primary" : "alternate",
    total: primaryWins ? primary.total : alternate.total,
  };
}
