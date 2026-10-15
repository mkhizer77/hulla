import { mkdirSync, readdirSync, readFileSync, writeFileSync } from "node:fs";
import { join } from "node:path";

import { argumentsOf, parse, type Part } from "./icu";
import { accented, PSEUDO_LOCALES } from "./pseudo";
import { rtlStyles } from "./rtl";
import { toSmart } from "./smart";

/**
 * content/strings/<locale>.json is the only source (GDD 26.1). This writes, per locale:
 * - the Unity table, Smart Strings by key, a Resources text asset the client formats with the
 *   Localization package's Smart Strings engine (ADR-0023);
 * - the web's copy, ICU as written, for the web client (ADR-0017).
 * Every locale must use the English keys' arguments; English is the fallback for missing keys.
 */
export interface StringsFile {
  readonly locale: string;
  readonly strings: Readonly<Record<string, string>>;
  readonly notes?: Readonly<Record<string, string>>;
}

const KEY = /^[a-z][a-z0-9_]*(\.[A-Za-z0-9_]+)+$/;

export function problemsOf(file: StringsFile, english: StringsFile | null): string[] {
  const problems: string[] = [];
  for (const [key, message] of Object.entries(file.strings)) {
    if (!KEY.test(key)) problems.push(`${file.locale}: bad key "${key}"`);
    let args: string[];
    try {
      args = argumentsOf(parse(message));
    } catch (error: unknown) {
      problems.push(`${file.locale}: ${key}: ${String(error)}`);
      continue;
    }
    if (english !== null) {
      const source = english.strings[key];
      if (source === undefined) {
        problems.push(`${file.locale}: ${key} is not an English key`);
        continue;
      }
      const expected = argumentsOf(parse(source)).sort().join(",");
      if (args.sort().join(",") !== expected) {
        problems.push(
          `${file.locale}: ${key} uses {${args.join(",")}}, English uses {${expected}}`,
        );
      }
    }
  }
  return problems;
}

/** Locales whose tables the shell mirrors. */
export const RTL_LOCALES: readonly string[] = ["ar", "ur", "fa", "he", PSEUDO_LOCALES.mirrored];

export function isRtl(locale: string): boolean {
  return RTL_LOCALES.includes(locale) || RTL_LOCALES.includes(locale.split("-")[0] ?? "");
}

export interface UnityTable {
  readonly locale: string;
  readonly rtl: boolean;
  readonly entries: readonly {
    readonly key: string;
    readonly text: string;
    readonly smart: boolean;
  }[];
}

export function unityTable(
  file: StringsFile,
  transform: (parts: readonly Part[]) => readonly Part[] = (parts) => parts,
): UnityTable {
  return {
    locale: file.locale,
    rtl: isRtl(file.locale),
    entries: Object.keys(file.strings)
      .sort()
      .map((key) => ({ key, ...toSmart(transform(parse(file.strings[key] ?? "")), file.locale) })),
  };
}

/** The pseudo-locales' tables, made from English (GDD 26.1). */
export function pseudoTables(english: StringsFile): UnityTable[] {
  return [
    unityTable({ ...english, locale: PSEUDO_LOCALES.accented }, accented),
    unityTable({ ...english, locale: PSEUDO_LOCALES.mirrored }),
  ];
}

function main(): void {
  const root = join(import.meta.dirname, "..", "..", "..");
  const source = join(root, "content", "strings");
  const unityDir = join(
    root,
    "apps",
    "unity",
    "Hulla",
    "Assets",
    "_Project",
    "Localization",
    "Resources",
    "Strings",
  );
  const webDir = join(root, "packages", "strings", "generated");
  const files = readdirSync(source)
    .filter((name) => name.endsWith(".json"))
    .sort()
    .map((name) => JSON.parse(readFileSync(join(source, name), "utf8")) as StringsFile);
  const english = files.find((f) => f.locale === "en");
  if (english === undefined) throw new Error("content/strings/en.json is missing");
  const problems = files.flatMap((f) => problemsOf(f, f === english ? null : english));
  if (problems.length > 0) {
    for (const problem of problems) console.error(problem);
    process.exit(1);
  }
  mkdirSync(unityDir, { recursive: true });
  mkdirSync(webDir, { recursive: true });
  for (const file of files) {
    writeFileSync(
      join(unityDir, `strings.${file.locale}.json`),
      `${JSON.stringify(unityTable(file), null, 1)}\n`,
    );
    const sorted = Object.fromEntries(
      Object.entries(file.strings).sort(([a], [b]) => a.localeCompare(b)),
    );
    writeFileSync(
      join(webDir, `${file.locale}.json`),
      `${JSON.stringify({ locale: file.locale, strings: sorted }, null, 1)}\n`,
    );
  }
  for (const table of pseudoTables(english)) {
    writeFileSync(
      join(unityDir, `strings.${table.locale}.json`),
      `${JSON.stringify(table, null, 1)}\n`,
    );
  }
  const theme = join(root, "apps", "unity", "Hulla", "Assets", "_Project", "Presentation");
  const sheets: Record<string, string> = {};
  for (const sheet of ["Theme/Hulla.uss", "Theme/Skins.uss", "Theme/Rooftop.uss", "Screens/Ranked/Ranked.uss"]) {
    sheets[sheet] = readFileSync(join(theme, sheet), "utf8");
  }
  writeFileSync(join(theme, "Theme", "Rtl.uss"), rtlStyles(sheets));
  console.log(
    `strings: ${String(files.length)} locale(s), ${String(Object.keys(english.strings).length)} keys`,
  );
}

if (process.argv[1]?.endsWith("export.ts") === true) main();
