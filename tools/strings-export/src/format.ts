import { pluralCategories } from "./smart";

import type { Part } from "./icu";

/** Formats a parsed message with arguments, as the web client will (and as the tests check English). */
export function format(
  parts: readonly Part[],
  args: Readonly<Record<string, string | number>>,
  locale = "en",
  pound: number | null = null,
): string {
  const rules = new Intl.PluralRules(locale);
  return parts
    .map((part) => {
      switch (part.type) {
        case "text":
          return part.value;
        case "arg":
          return String(args[part.name] ?? `{${part.name}}`);
        case "number":
          return new Intl.NumberFormat(locale, { maximumFractionDigits: 0 }).format(
            Number(args[part.name] ?? 0),
          );
        case "pound":
          return pound === null ? "#" : new Intl.NumberFormat(locale).format(pound);
        case "plural": {
          const n = Number(args[part.name] ?? 0);
          const category = rules.select(n);
          const known = pluralCategories(locale).includes(category) ? category : "other";
          const chosen = part.options[known] ?? part.options["other"] ?? [];
          return format(chosen, args, locale, n);
        }
        case "select": {
          const value = String(args[part.name] ?? "other");
          return format(part.options[value] ?? part.options["other"] ?? [], args, locale, pound);
        }
      }
    })
    .join("");
}
