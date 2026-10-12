import { readdirSync, readFileSync, statSync } from "node:fs";
import { dirname, join, relative, resolve } from "node:path";

import { defaultRules, THULLA_PRESETS, THULLA_RULES_SCHEMA } from "@hulla/rules-engine";
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

/** Presets must stay inside the engine's rules schema and equal its own preset table (CODING_RULES 8). */
function presetProblems(file: ContentFile, rulesCheck: ValidateFunction): string[] {
  const problems: string[] = [];
  const id = String(file.document["id"]);
  const overrides = file.document["overrides"];
  if (!rulesCheck({ ...defaultRules("private"), ...(overrides as object) })) {
    problems.push(
      `${file.path}: overrides are outside the Thulla rules schema: ${describe(rulesCheck)}`,
    );
  }
  const expected = (THULLA_PRESETS as Record<string, unknown>)[id];
  if (expected === undefined) {
    problems.push(`${file.path}: the engine has no preset ${id}`);
  } else if (JSON.stringify(expected) !== JSON.stringify(overrides)) {
    problems.push(
      `${file.path}: overrides differ from the engine's THULLA_PRESETS.${id}: ${JSON.stringify(expected)}`,
    );
  }
  return problems;
}

/** Every content file names its schema; a preset is also checked against the engine. */
export function validateContentTree(contentDir: string): string[] {
  const compile = compiler();
  const ajvRules = new AjvDraft7({ strictRequired: false, allowUnionTypes: true }).compile(
    THULLA_RULES_SCHEMA,
  );
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
    if (schemaPath.endsWith("preset.schema.json") && document["gameId"] === "thulla") {
      problems.push(...presetProblems(file, ajvRules));
    }
  }
  return problems;
}
