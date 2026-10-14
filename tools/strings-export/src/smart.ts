import { PLURAL_CATEGORIES, type Part } from "./icu";

/**
 * ICU to Unity Smart Strings (com.unity.localization): `{n, plural, one {# card} other {# cards}}`
 * becomes `{n:plural:{} card|{} cards}` with the locale's plural categories in CLDR order, and
 * `{g, select, a {..} other {..}}` becomes `{g:choose(a):..|..}`. Plain messages stay plain.
 */
const LOCALE_PLURALS: Readonly<Record<string, readonly string[]>> = {
  en: ["one", "other"],
  ur: ["one", "other"],
  hi: ["one", "other"],
  bn: ["one", "other"],
  pa: ["one", "other"],
  ar: ["zero", "one", "two", "few", "many", "other"],
};

export function pluralCategories(locale: string): readonly string[] {
  return LOCALE_PLURALS[locale.split("-")[0] ?? locale] ?? ["one", "other"];
}

function escapeText(text: string, nested: boolean): string {
  const braces = text.replace(/[{}]/g, (c) => `\\${c}`);
  return nested ? braces.replace(/\|/g, "\\|") : braces;
}

function render(parts: readonly Part[], locale: string, nested: boolean): string {
  return parts
    .map((part) => {
      switch (part.type) {
        case "text":
          return escapeText(part.value, nested);
        case "arg":
          return `{${part.name}}`;
        case "number":
          return `{${part.name}:N0}`;
        case "pound":
          return "{}";
        case "plural": {
          const forms = pluralCategories(locale)
            .filter((c) => (PLURAL_CATEGORIES as readonly string[]).includes(c))
            .map((c) => render(part.options[c] ?? part.options["other"] ?? [], locale, true));
          return `{${part.name}:plural:${forms.join("|")}}`;
        }
        case "select": {
          const keys = Object.keys(part.options).filter((k) => k !== "other");
          const forms = [...keys, "other"].map((k) => render(part.options[k] ?? [], locale, true));
          return `{${part.name}:choose(${keys.join("|")}):${forms.join("|")}}`;
        }
      }
    })
    .join("");
}

export interface SmartEntry {
  readonly text: string;
  /** True when the entry takes arguments or has braces: Smart Strings formats it, plain text is left alone. */
  readonly smart: boolean;
}

export function toSmart(parts: readonly Part[], locale: string): SmartEntry {
  const smart = parts.some((p) => p.type !== "text" || /[{}]/.test(p.value));
  return { text: render(parts, locale, false), smart };
}
