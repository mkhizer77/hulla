import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";

import { Ajv } from "ajv";
import { describe, expect, it } from "vitest";

import { runRungVector, type RungVector } from "../index";

const root = join(import.meta.dirname, "..", "..");
const schema = JSON.parse(
  readFileSync(join(root, "schema", "rung-vector.schema.json"), "utf8"),
) as object;
const validate = new Ajv({ strict: true, strictRequired: false, allErrors: true }).compile(schema);
const files = readdirSync(join(root, "rung"))
  .filter((name) => name.startsWith("V-") && name.endsWith(".json"))
  .sort();
const vectors: RungVector[] = files.map(
  (name) => JSON.parse(readFileSync(join(root, "rung", name), "utf8")) as RungVector,
);

describe("shared Rung vectors", () => {
  it("has at least 50 vectors with unique ids that match their file names", () => {
    expect(vectors.length).toBeGreaterThanOrEqual(50);
    expect(new Set(vectors.map((vector) => vector.id)).size).toBe(vectors.length);
    vectors.forEach((vector, index) => {
      expect(`${vector.id}.json`).toBe(files[index]);
    });
  });

  it("every vector validates against schema/rung-vector.schema.json and carries a hash", () => {
    for (const vector of vectors) {
      const valid = validate(vector);
      expect(valid, `${vector.id}: ${JSON.stringify(validate.errors)}`).toBe(true);
      expect(vector.assert.stateHash ?? vector.assert.seriesHash, vector.id).toBeDefined();
    }
  });

  it("a wrong assertion, a wrong hash and a wrong version are reported", () => {
    const base = vectors.find((vector) => vector.id === "V-R030-pile");
    if (base === undefined) throw new Error("V-R030-pile is missing");
    expect(runRungVector({ ...base, assert: { ...base.assert, pile: 2 } }).failures).toHaveLength(
      1,
    );
    expect(runRungVector({ ...base, assert: { stateHash: "0".repeat(64) } }).failures[0]).toContain(
      "stateHash",
    );
    expect(runRungVector({ ...base, version: "0.0.0" }).failures[0]).toContain("version");
    const { id, version, rules, moves, assert } = base;
    expect(runRungVector({ id, version, rules, moves, assert }).failures[0]).toContain(
      "needs a deal",
    );
  });

  for (const vector of vectors) {
    it(`${vector.id} ${vector.title ?? ""} [${vector.rules.join(" ")}]`, () => {
      expect(runRungVector(vector).failures).toEqual([]);
    });
  }
});
