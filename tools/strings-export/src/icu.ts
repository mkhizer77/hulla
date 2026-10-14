/**
 * The ICU MessageFormat subset the string tables allow (CODING_RULES 9): text, simple arguments
 * `{name}`, locale-grouped numbers `{n, number}` and `{n, number, integer}`, `{n, plural, ...}`
 * with `#` and `{g, select, ...}`. Apostrophes quote only before a
 * brace, `#` or `|` (ICU's DOUBLE_OPTIONAL mode); `''` is one apostrophe.
 */
export type Part =
  | { readonly type: "text"; readonly value: string }
  | { readonly type: "arg"; readonly name: string }
  /** `{n, number}` or `{n, number, integer}`: grouped by the locale's rules. */
  | { readonly type: "number"; readonly name: string; readonly style: "integer" | null }
  | { readonly type: "pound" }
  | {
      readonly type: "plural" | "select";
      readonly name: string;
      readonly options: Readonly<Record<string, readonly Part[]>>;
    };

export const PLURAL_CATEGORIES = ["zero", "one", "two", "few", "many", "other"] as const;
const NAME = /^[A-Za-z0-9_]+$/;

export class IcuError extends Error {}

class Parser {
  private index = 0;

  constructor(private readonly source: string) {}

  parse(): Part[] {
    const parts = this.message(0, false);
    if (this.index < this.source.length) this.fail("unexpected }");
    return parts;
  }

  private fail(reason: string): never {
    throw new IcuError(`${reason} at ${String(this.index)} in "${this.source}"`);
  }

  private message(depth: number, inPlural: boolean): Part[] {
    const parts: Part[] = [];
    let text = "";
    const flush = (): void => {
      if (text !== "") parts.push({ type: "text", value: text });
      text = "";
    };
    while (this.index < this.source.length) {
      const ch = this.source[this.index] ?? "";
      if (ch === "'") {
        const next = this.source[this.index + 1] ?? "";
        if (next === "'") {
          text += "'";
          this.index += 2;
          continue;
        }
        if (next === "{" || next === "}" || next === "|" || (inPlural && next === "#")) {
          const end = this.source.indexOf("'", this.index + 1);
          if (end === -1) this.fail("unclosed quote");
          text += this.source.slice(this.index + 1, end);
          this.index = end + 1;
          continue;
        }
        text += ch;
        this.index += 1;
        continue;
      }
      if (ch === "}") {
        if (depth === 0) this.fail("unexpected }");
        break;
      }
      if (ch === "#" && inPlural) {
        flush();
        parts.push({ type: "pound" });
        this.index += 1;
        continue;
      }
      if (ch === "{") {
        flush();
        parts.push(this.argument(depth, inPlural));
        continue;
      }
      text += ch;
      this.index += 1;
    }
    flush();
    return parts;
  }

  private argument(depth: number, inPlural: boolean): Part {
    this.index += 1;
    const close = this.source.indexOf("}", this.index);
    const comma = this.source.indexOf(",", this.index);
    if (close === -1) this.fail("unclosed argument");
    if (comma === -1 || close < comma) {
      const name = this.source.slice(this.index, close).trim();
      if (!NAME.test(name)) this.fail(`bad argument name "${name}"`);
      this.index = close + 1;
      return { type: "arg", name };
    }
    const name = this.source.slice(this.index, comma).trim();
    if (!NAME.test(name)) this.fail(`bad argument name "${name}"`);
    this.index = comma + 1;
    const typeEnd = this.source.indexOf(",", this.index);
    const head = this.source.slice(this.index, close).trim();
    if (/^number(\s*,\s*[a-z]+)?$/.test(head)) {
      const style = head === "number" ? null : head.slice(head.indexOf(",") + 1).trim();
      if (style !== null && style !== "integer") this.fail(`unsupported number style "${style}"`);
      this.index = close + 1;
      return { type: "number", name, style: style === "integer" ? "integer" : null };
    }
    if (typeEnd === -1) this.fail("argument without options");
    const kind = this.source.slice(this.index, typeEnd).trim();
    if (kind !== "plural" && kind !== "select") this.fail(`unsupported argument type "${kind}"`);
    this.index = typeEnd + 1;
    const options: Record<string, Part[]> = {};
    for (;;) {
      this.skipSpace();
      const ch = this.source[this.index];
      if (ch === "}") {
        this.index += 1;
        break;
      }
      if (ch === undefined) this.fail("unclosed options");
      const open = this.source.indexOf("{", this.index);
      if (open === -1) this.fail("option without a message");
      const key = this.source.slice(this.index, open).trim();
      if (kind === "plural" && !(PLURAL_CATEGORIES as readonly string[]).includes(key)) {
        this.fail(`plural option "${key}" is not a CLDR category`);
      }
      if (kind === "select" && !NAME.test(key)) this.fail(`bad select option "${key}"`);
      if (key in options) this.fail(`option "${key}" twice`);
      this.index = open + 1;
      options[key] = this.message(depth + 1, kind === "plural" || inPlural);
      if (this.source[this.index] !== "}") this.fail("unclosed option");
      this.index += 1;
    }
    if (!("other" in options)) this.fail(`${kind} without "other"`);
    return { type: kind, name, options };
  }

  private skipSpace(): void {
    while (/\s/.test(this.source[this.index] ?? "")) this.index += 1;
  }
}

export function parse(message: string): Part[] {
  return new Parser(message).parse();
}

/** Every argument name a message uses, in first-use order. */
export function argumentsOf(parts: readonly Part[]): string[] {
  const names: string[] = [];
  const visit = (list: readonly Part[]): void => {
    for (const part of list) {
      if (part.type !== "text" && part.type !== "pound") {
        if (!names.includes(part.name)) names.push(part.name);
      }
      if (part.type === "plural" || part.type === "select") {
        for (const option of Object.values(part.options)) visit(option);
      }
    }
  };
  visit(parts);
  return names;
}
