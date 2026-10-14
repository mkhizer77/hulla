import { readdirSync, readFileSync, statSync } from "node:fs";
import { join, relative } from "node:path";

/**
 * No hard-coded user-facing text in the Unity client (CODING_RULES 8, GDD 26.1). In C# under
 * Presentation, Games and Composition (tests excepted), a string literal with letters must look
 * like an id, key, style class, path, format or log line; anything else is text that belongs in
 * content/strings. In UXML a `text` attribute must be empty. Keys used literally must exist.
 */
const SCANNED = ["Presentation", "Games", "Composition"];
/** Ids, keys, error codes, element names and style classes: "ui.back", "chip--on", "nav-home". */
const IDENTIFIER = /^[a-z0-9_.:/#-]*$/;
/**
 * Tokens that are never prose: format strings, card codes ("7S"), camelCase ids, SCREAMING_CASE,
 * dotted or slashed names ("android.os.Build$VERSION"), URLs. A plain capitalised word ("Back")
 * still counts as text.
 */
const TOKEN =
  /^(N0|F[0-9]|D[0-9]|[0-9A-Z]{1,2}[SHDC]|[a-z][a-z0-9]*[A-Z][A-Za-z0-9]*|[A-Z][A-Z0-9_]+|[A-Za-z0-9_$]+([./$][A-Za-z0-9_$]+)+|[a-z]+:\/\/\S*)$/;
/** Lines that are logs, exceptions, asset paths or attributes, never shown to a player. */
const NOT_SHOWN =
  /InternalsVisibleTo|new GameObject\(|SceneManager|Debug\.Log|LogWarning|LogError|throw new|new Exception|Exception\(|AssetDatabase|Resources\.Load|PlayerPrefs|nameof\(|\[[A-Z][A-Za-z]*\(|Shader\.|Q<|Q\(|UQueryExtensions|AddToClassList|RemoveFromClassList|EnableInClassList|ClassListContains|name = "|\.name =|\.name [!=]=|Environment\.|GetEnvironmentVariable|Path\.Combine|Application\.(OpenURL|persistentDataPath)/;

export interface LintProblem {
  readonly file: string;
  readonly line: number;
  readonly text: string;
}

function walk(dir: string, ext: string): string[] {
  return readdirSync(dir).flatMap((entry) => {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) return entry === "Tests" ? [] : walk(full, ext);
    return full.endsWith(ext) ? [full] : [];
  });
}

/** The string literals on one line of C#, comments removed; interpolated holes are dropped. */
export function literalsOf(line: string): string[] {
  const out: string[] = [];
  let i = 0;
  while (i < line.length) {
    if (line.startsWith("//", i)) break;
    const verbatim =
      line.startsWith('@"', i) || line.startsWith('$@"', i) || line.startsWith('@$"', i);
    const interpolated = line.startsWith('$"', i);
    if (line[i] === '"' || verbatim || interpolated) {
      i += verbatim ? (line[i] === "@" ? 2 : 3) : interpolated ? 2 : 1;
      let text = "";
      let depth = 0;
      while (i < line.length) {
        const ch = line[i] ?? "";
        if (!verbatim && ch === "\\") {
          text += line[i + 1] ?? "";
          i += 2;
          continue;
        }
        if ((interpolated || verbatim) && ch === "{" && line[i + 1] !== "{") depth += 1;
        if (depth > 0) {
          if (ch === "}") depth -= 1;
          i += 1;
          continue;
        }
        if (ch === '"') {
          if (verbatim && line[i + 1] === '"') {
            text += '"';
            i += 2;
            continue;
          }
          break;
        }
        text += ch;
        i += 1;
      }
      out.push(text);
      i += 1;
      continue;
    }
    if (line[i] === "'") {
      const end = line.indexOf("'", i + 1);
      i = end === -1 ? line.length : end + 1;
      continue;
    }
    i += 1;
  }
  return out;
}

export function isUserText(literal: string): boolean {
  if (!/\p{L}/u.test(literal)) return false;
  const trimmed = literal.trim();
  if (IDENTIFIER.test(trimmed) || TOKEN.test(trimmed)) return false;
  return true;
}

export function hardcodedText(unityProject: string): LintProblem[] {
  const root = join(unityProject, "Assets", "_Project");
  const problems: LintProblem[] = [];
  for (const area of SCANNED) {
    for (const file of walk(join(root, area), ".cs")) {
      const rel = relative(root, file);
      if (rel.startsWith(join("Presentation", "Text"))) continue;
      readFileSync(file, "utf8")
        .split("\n")
        .forEach((line, index) => {
          const code = line.trim();
          if (
            code.startsWith("//") ||
            code.startsWith("///") ||
            code.startsWith("*") ||
            NOT_SHOWN.test(line)
          )
            return;
          for (const literal of literalsOf(line)) {
            if (isUserText(literal)) problems.push({ file: rel, line: index + 1, text: literal });
          }
          // A unit glued to a number ("12" + "s") or an English ordinal reads as text too.
          const unit = /[\w)\]]\s*\+\s*"([a-z]{1,3})"|"(\d+(?:st|nd|rd|th))"/.exec(line);
          if (unit !== null)
            problems.push({ file: rel, line: index + 1, text: unit[1] ?? unit[2] ?? "" });
        });
    }
    for (const file of walk(join(root, area), ".uxml")) {
      const rel = relative(root, file);
      readFileSync(file, "utf8")
        .split("\n")
        .forEach((line, index) => {
          for (const match of line.matchAll(/\b(text|label|tooltip)="([^"]*)"/g)) {
            if (/\p{L}/u.test(match[2] ?? ""))
              problems.push({ file: rel, line: index + 1, text: match[2] ?? "" });
          }
        });
    }
  }
  return problems;
}

/** Keys the client spells out (`Strings.Text("ui.back")`) that the English table lacks. */
export function unknownKeys(unityProject: string, keys: ReadonlySet<string>): LintProblem[] {
  const root = join(unityProject, "Assets", "_Project");
  const problems: LintProblem[] = [];
  for (const file of walk(root, ".cs")) {
    readFileSync(file, "utf8")
      .split("\n")
      .forEach((line, index) => {
        for (const match of line.matchAll(/Strings\.(?:Text|Get|Has)\("([^"]+)"/g)) {
          const key = match[1] ?? "";
          if (!key.endsWith(".") && !keys.has(key)) {
            problems.push({ file: relative(root, file), line: index + 1, text: key });
          }
        }
      });
  }
  return problems;
}
