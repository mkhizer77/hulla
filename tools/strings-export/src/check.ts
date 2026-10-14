import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";

import { problemsOf, type StringsFile } from "./export";

/**
 * `pnpm content:validate`: the strings parse, translations keep the English arguments, and every
 * `name`, `description` or `title` key a content file points at exists in English (GDD 26.1).
 */
export function contentKeyProblems(contentDir: string, english: StringsFile): string[] {
  const problems: string[] = [];
  const walk = (dir: string): string[] =>
    readdirSync(dir).flatMap((entry) => {
      const full = join(dir, entry);
      return statSync(full).isDirectory() ? walk(full) : full.endsWith(".json") ? [full] : [];
    });
  const visit = (value: unknown, file: string): void => {
    if (Array.isArray(value)) {
      for (const item of value) visit(item, file);
      return;
    }
    if (typeof value !== "object" || value === null) return;
    for (const [field, inner] of Object.entries(value as Record<string, unknown>)) {
      if (
        (field === "name" || field === "description") &&
        typeof inner === "string" &&
        /^[a-z]+\.[A-Za-z0-9_.]+$/.test(inner) &&
        english.strings[inner] === undefined
      ) {
        problems.push(`${file}: "${inner}" has no English string`);
      }
      if (field === "title" && typeof inner === "string" && /^[a-z0-9_]+$/.test(inner)) {
        if (english.strings[`title.${inner}`] === undefined) {
          problems.push(`${file}: title "${inner}" has no English string (title.${inner})`);
        }
      }
      visit(inner, file);
    }
  };
  for (const path of walk(contentDir)) {
    const rel = relative(contentDir, path);
    if (rel.startsWith("strings") || rel === "manifest.json" || rel.startsWith("quick-chat"))
      continue;
    visit(JSON.parse(readFileSync(path, "utf8")) as unknown, rel);
  }
  return problems;
}

function main(): void {
  const content = join(import.meta.dirname, "..", "..", "..", "content");
  const dir = join(content, "strings");
  const files = readdirSync(dir)
    .filter((n) => n.endsWith(".json"))
    .map((n) => JSON.parse(readFileSync(join(dir, n), "utf8")) as StringsFile);
  const english = files.find((f) => f.locale === "en");
  if (english === undefined) throw new Error("content/strings/en.json is missing");
  const problems = [
    ...files.flatMap((f) => problemsOf(f, f === english ? null : english)),
    ...contentKeyProblems(content, english),
  ];
  for (const problem of problems) console.error(problem);
  if (problems.length > 0) {
    console.error(`strings: ${String(problems.length)} problem(s)`);
    process.exit(1);
  }
  console.log("strings: every key parses and every content key has English");
}

if (process.argv[1]?.endsWith("check.ts") === true) main();
