import { cpSync, mkdtempSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { join } from "node:path";

import { describe, expect, it } from "vitest";

import { validateContentTree } from "../content";

const contentDir = join(import.meta.dirname, "..", "..", "..", "..", "content");
const schemaPath = join(
  import.meta.dirname,
  "..",
  "..",
  "..",
  "content-schemas",
  "preset.schema.json",
);

function tree(files: Record<string, unknown>): string {
  const dir = mkdtempSync(join(tmpdir(), "hulla-content-"));
  for (const [name, doc] of Object.entries(files)) {
    mkdirSync(join(dir, "presets", "thulla"), { recursive: true });
    writeFileSync(
      join(dir, "presets", "thulla", name),
      typeof doc === "string" ? doc : JSON.stringify(doc),
    );
  }
  return dir;
}

describe("content validation", () => {
  it("accepts the repository's content", () => {
    expect(validateContentTree(contentDir)).toEqual([]);
  });

  it("reports a preset outside the rules schema, one that drifts from the engine, bad JSON and a missing schema", () => {
    const base = {
      schema: schemaPath,
      id: "family",
      version: 1,
      gameId: "thulla",
      name: "preset.family.name",
      description: "preset.family.description",
      order: 1,
      settings: {},
    };
    const problems = validateContentTree(
      tree({
        "outside.json": { ...base, overrides: { handTaking: "leaderOnly", deckCount: 9 } },
        "drift.json": { ...base, overrides: {} },
        "broken.json": "{",
        "noschema.json": { id: "standard" },
        "badsettings.json": {
          ...base,
          overrides: { handTaking: "leaderOnly" },
          settings: { turnTimerSeconds: 7 },
        },
      }),
    );
    expect(problems.some((p) => p.includes("outside.json") && p.includes("rules schema"))).toBe(
      true,
    );
    expect(
      problems.some((p) => p.includes("drift.json") && p.includes("THULLA_PRESETS.family")),
    ).toBe(true);
    expect(problems.some((p) => p.includes("broken.json") && p.includes("not JSON"))).toBe(true);
    expect(problems.some((p) => p.includes("noschema.json") && p.includes('"schema"'))).toBe(true);
    expect(problems.some((p) => p.includes("badsettings.json"))).toBe(true);
  });

  it("requires an id and an integer version on every file", () => {
    const dir = tree({
      "noversion.json": {
        schema: schemaPath,
        id: "standard",
        gameId: "thulla",
        name: "preset.standard.name",
        description: "preset.standard.description",
        order: 0,
        overrides: {},
        settings: {},
      },
    });
    const problems = validateContentTree(dir);
    expect(problems.some((p) => p.includes('no integer "version" field'))).toBe(true);
  });
  it("reports ranked leagues that fall back and rewards that are not earn-only", () => {
    const dir = mkdtempSync(join(tmpdir(), "hulla-ranked-"));
    for (const folder of ["ranked", "catalog", "avatars"]) {
      cpSync(join(contentDir, folder), join(dir, folder), { recursive: true });
    }
    const path = join(dir, "ranked", "v1.json");
    const ranked = JSON.parse(readFileSync(path, "utf8")) as {
      schema: string;
      leagues: { minRating: number; reward: { items?: string[]; frame?: string } }[];
    };
    ranked.schema = join(contentDir, "..", "tools", "content-schemas", "ranked.schema.json");
    const [first, second] = ranked.leagues;
    if (first === undefined || second === undefined) throw new Error("no leagues");
    first.minRating = 5000;
    second.reward.items = ["back_classic_red"];
    second.reward.frame = "f01";
    writeFileSync(path, JSON.stringify(ranked));
    const problems = validateContentTree(dir).filter((p) => p.startsWith("ranked"));
    expect(problems).toHaveLength(3);
    expect(problems.some((p) => p.includes("starts below"))).toBe(true);
    expect(problems.some((p) => p.includes("back_classic_red"))).toBe(true);
    expect(problems.some((p) => p.includes("f01"))).toBe(true);
  });
});
