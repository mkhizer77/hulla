import { readFileSync, readdirSync } from "node:fs";
import { join } from "node:path";

import { Ajv } from "ajv";
import { describe, expect, it } from "vitest";

import { runVector, type Vector } from "../index";

const root = join(import.meta.dirname, "..", "..");
const schema = JSON.parse(
  readFileSync(join(root, "schema", "vector.schema.json"), "utf8"),
) as object;
const validate = new Ajv({ strict: true, strictRequired: false, allErrors: true }).compile(schema);
const files = readdirSync(join(root, "thulla"))
  .filter((name) => name.startsWith("V-") && name.endsWith(".json"))
  .sort();
const vectors: Vector[] = files.map(
  (name) => JSON.parse(readFileSync(join(root, "thulla", name), "utf8")) as Vector,
);

describe("shared Thulla vectors", () => {
  it("has at least 150 vectors with unique ids that match their file names", () => {
    expect(vectors.length).toBeGreaterThanOrEqual(150);
    expect(new Set(vectors.map((vector) => vector.id)).size).toBe(vectors.length);
    vectors.forEach((vector, index) => {
      expect(`${vector.id}.json`).toBe(files[index]);
    });
  });

  it("every vector validates against schema/vector.schema.json", () => {
    for (const vector of vectors) {
      const valid = validate(vector);
      expect(valid, `${vector.id}: ${JSON.stringify(validate.errors)}`).toBe(true);
    }
  });

  for (const vector of vectors) {
    it(`${vector.id} ${vector.title ?? ""} [${vector.rules.join(" ")}]`, () => {
      expect(runVector(vector).failures).toEqual([]);
    });
  }
});
