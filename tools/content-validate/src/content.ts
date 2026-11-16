import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";

import {
  defaultRules,
  defaultRungRules,
  RUNG_PRESETS,
  RUNG_RULES_SCHEMA,
  THULLA_PRESETS,
  THULLA_RULES_SCHEMA,
} from "@hulla/rules-engine";
import AjvDraft7, { type ValidateFunction } from "ajv";

interface ContentFile {
  readonly path: string;
  readonly document: Record<string, unknown>;
}

function walk(dir: string, out: string[] = []): string[] {
  for (const entry of readdirSync(dir).sort()) {
    const full = join(dir, entry);
    if (statSync(full).isDirectory()) walk(full, out);
    else if (entry.endsWith(".json")) out.push(full);
  }
  return out;
}

function compiler(): (schemaPath: string) => ValidateFunction {
  const ajv = new AjvDraft7({ strictRequired: false, allowUnionTypes: true });
  ajv.addSchema(THULLA_RULES_SCHEMA);
  const cache = new Map<string, ValidateFunction>();
  return (schemaPath) => {
    const cached = cache.get(schemaPath);
    if (cached !== undefined) return cached;
    const schema = JSON.parse(readFileSync(schemaPath, "utf8")) as Record<string, unknown>;
    const compiled = ajv.compile(schema);
    cache.set(schemaPath, compiled);
    return compiled;
  };
}

function describe(validate: ValidateFunction): string {
  return (validate.errors ?? [])
    .map((e) => `${e.instancePath === "" ? "/" : e.instancePath} ${e.message ?? e.keyword}`)
    .join("; ");
}

/** What a game's presets are checked against: its rules schema, defaults and own preset table. */
interface PresetGame {
  readonly name: string;
  /** The engine's preset table, named in the report. */
  readonly table: string;
  readonly check: ValidateFunction;
  readonly defaults: object;
  readonly presets: Readonly<Record<string, unknown>>;
}

/** Presets must stay inside the engine's rules schema and equal its own preset table (CODING_RULES 8). */
function presetProblems(file: ContentFile, game: PresetGame): string[] {
  const problems: string[] = [];
  const id = String(file.document["id"]);
  const overrides = file.document["overrides"];
  if (!game.check({ ...game.defaults, ...(overrides as object) })) {
    problems.push(
      `${file.path}: overrides are outside the ${game.name} rules schema: ${describe(game.check)}`,
    );
  }
  const expected = game.presets[id];
  if (expected === undefined) {
    problems.push(`${file.path}: the engine has no preset ${id}`);
  } else if (JSON.stringify(expected) !== JSON.stringify(overrides)) {
    problems.push(
      `${file.path}: overrides differ from the engine's ${game.table}.${id}: ${JSON.stringify(expected)}`,
    );
  }
  return problems;
}

/**
 * The ranked ladder's leagues climb, and its rewards name earn-only card backs from the catalog and
 * earn-only frames from the avatars file (GDD 7.5, 12.9).
 */
function rankedProblems(file: ContentFile, contentDir: string): string[] {
  const problems: string[] = [];
  const read = (rel: string): Record<string, unknown> =>
    JSON.parse(readFileSync(join(contentDir, rel), "utf8")) as Record<string, unknown>;
  const earnOnly = (list: unknown): Set<string> =>
    new Set(
      (Array.isArray(list) ? (list as Record<string, unknown>[]) : [])
        .filter((entry) => entry["earnOnly"] === true)
        .map((entry) => String(entry["id"])),
    );
  const backs = earnOnly(read("catalog/v1.json")["items"]);
  const frames = earnOnly(read("avatars/v1.json")["frames"]);
  const leagues = file.document["leagues"] as {
    id: string;
    minRating: number;
    reward: { items?: string[]; frame?: string };
  }[];
  leagues.forEach((league, index) => {
    const before = leagues[index - 1];
    if (before !== undefined && league.minRating < before.minRating) {
      problems.push(`${file.path}: league ${league.id} starts below ${before.id}`);
    }
    for (const item of league.reward.items ?? []) {
      if (!backs.has(item)) problems.push(`${file.path}: ${item} is not an earn-only catalog item`);
    }
    const frame = league.reward.frame;
    if (frame !== undefined && !frames.has(frame)) {
      problems.push(`${file.path}: ${frame} is not an earn-only frame`);
    }
  });
  return problems;
}

/** Every content file names its schema; a preset is also checked against the engine. */
export function validateContentTree(contentDir: string): string[] {
  const compile = compiler();
  const rulesAjv = new AjvDraft7({ strictRequired: false, allowUnionTypes: true });
  const games: Readonly<Record<string, PresetGame>> = {
    thulla: {
      name: "Thulla",
      table: "THULLA_PRESETS",
      check: rulesAjv.compile(THULLA_RULES_SCHEMA),
      defaults: defaultRules("private"),
      presets: THULLA_PRESETS,
    },
    rung: {
      name: "Rung",
      table: "RUNG_PRESETS",
      check: rulesAjv.compile(RUNG_RULES_SCHEMA),
      defaults: defaultRungRules("private"),
      presets: RUNG_PRESETS,
    },
  };
  const problems: string[] = [];
  for (const path of walk(contentDir)) {
    const rel = relative(contentDir, path);
    let document: Record<string, unknown>;
    try {
      document = JSON.parse(readFileSync(path, "utf8")) as Record<string, unknown>;
    } catch (error: unknown) {
      problems.push(`${rel}: not JSON (${String(error)})`);
      continue;
    }
    const schemaRef = document["schema"];
    if (typeof schemaRef !== "string") {
      problems.push(`${rel}: no "schema" field`);
      continue;
    }
    // Every content file is addressable and versioned (PROJECT_STRUCTURE 2, ADR-0011).
    if (typeof document["id"] !== "string" || document["id"] === "") {
      problems.push(`${rel}: no "id" field`);
    }
    const version = document["version"];
    if (typeof version !== "number" || !Number.isInteger(version) || version < 1) {
      problems.push(`${rel}: no integer "version" field`);
    }
    const schemaPath = resolve(dirname(path), schemaRef);
    let validate: ValidateFunction;
    try {
      validate = compile(schemaPath);
    } catch (error: unknown) {
      problems.push(`${rel}: schema ${schemaRef} cannot be loaded (${String(error)})`);
      continue;
    }
    if (!validate(document)) {
      problems.push(`${rel}: ${describe(validate)}`);
      continue;
    }
    const file: ContentFile = { path: rel, document };
    const game = games[String(document["gameId"])];
    if (schemaPath.endsWith("preset.schema.json") && game !== undefined) {
      problems.push(...presetProblems(file, game));
    }
    if (schemaPath.endsWith("ranked.schema.json")) {
      problems.push(...rankedProblems(file, contentDir));
    }
  }
  return problems;
}
