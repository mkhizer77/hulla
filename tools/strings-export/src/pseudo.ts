import type { Part } from "./icu";

/**
 * Pseudo-locales for the layout check (GDD 26.1): en-XA accents the Latin letters, grows every
 * message by 30% and brackets it, so clipped, missing or concatenated text shows; ar-XB keeps the
 * English but marks the table right-to-left, so the mirrored layout can be checked.
 */
const ACCENTS: Readonly<Record<string, string>> = {
  a: "å",
  c: "ç",
  e: "é",
  i: "î",
  n: "ñ",
  o: "ö",
  u: "û",
  y: "ý",
  A: "Å",
  C: "Ç",
  E: "É",
  I: "Î",
  N: "Ñ",
  O: "Ö",
  U: "Û",
  Y: "Ý",
};

export const PSEUDO_LOCALES = { accented: "en-XA", mirrored: "ar-XB" } as const;
export const EXPANSION = 0.3;

function visibleLength(parts: readonly Part[]): number {
  return parts.reduce((sum, part) => {
    if (part.type === "text") return sum + part.value.length;
    if (part.type === "plural" || part.type === "select") {
      return sum + visibleLength(part.options["other"] ?? []);
    }
    return sum + 3;
  }, 0);
}

function accent(parts: readonly Part[]): Part[] {
  return parts.map((part) => {
    if (part.type === "text") {
      return {
        type: "text",
        value: part.value.replace(/[aceinouyACEINOUY]/g, (ch) => ACCENTS[ch] ?? ch),
      };
    }
    if (part.type === "plural" || part.type === "select") {
      const options: Record<string, Part[]> = {};
      for (const [key, value] of Object.entries(part.options)) options[key] = accent(value);
      return { ...part, options };
    }
    return part;
  });
}

/** The accented, 30% longer, bracketed form of a message. */
export function accented(parts: readonly Part[]): Part[] {
  const grow = Math.max(1, Math.ceil(visibleLength(parts) * EXPANSION));
  const padding = Array.from({ length: Math.ceil(grow / 2) }, () => "·").join(" ");
  return [{ type: "text", value: "[" }, ...accent(parts), { type: "text", value: ` ${padding}]` }];
}
