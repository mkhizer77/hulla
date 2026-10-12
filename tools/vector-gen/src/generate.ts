import { mkdirSync, readdirSync, unlinkSync, writeFileSync } from "node:fs";
import { join } from "node:path";

import { THULLA_RULES_VERSION } from "@hulla/rules-engine";
import { runVector, stateHash, type Vector } from "@hulla/test-vectors";

import { authored } from "./authored";
import { endgameVectors } from "./endgames";

const outDir = join(import.meta.dirname, "..", "..", "..", "packages", "test-vectors", "thulla");
mkdirSync(outDir, { recursive: true });

const vectors: Vector[] = [
  ...authored(THULLA_RULES_VERSION),
  ...endgameVectors(THULLA_RULES_VERSION),
];
const ids = new Set<string>();
let failed = 0;
const written: string[] = [];
for (const vector of vectors) {
  if (ids.has(vector.id)) throw new Error(`duplicate id ${vector.id}`);
  ids.add(vector.id);
  const outcome = runVector(vector);
  if (outcome.failures.length > 0 || outcome.state === null) {
    failed += 1;
    console.error(`${vector.id}:\n  ${outcome.failures.join("\n  ")}`);
    continue;
  }
  const assert = {
    ...vector.assert,
    ...(vector.deal !== undefined && vector.assert.commitment === undefined
      ? { commitment: outcome.state.commitment }
      : {}),
    stateHash: stateHash(outcome.state),
  };
  const file: Vector = {
    id: vector.id,
    version: vector.version,
    ...(vector.title !== undefined ? { title: vector.title } : {}),
    rules: vector.rules,
    ...(vector.deal !== undefined ? { deal: vector.deal } : {}),
    ...(vector.setup !== undefined ? { setup: vector.setup } : {}),
    moves: vector.moves,
    assert,
  };
  const name = `${vector.id}.json`;
  writeFileSync(join(outDir, name), `${JSON.stringify(file, null, 2)}\n`);
  written.push(name);
}
for (const stale of readdirSync(outDir).filter(
  (n) => n.startsWith("V-") && n.endsWith(".json") && !written.includes(n),
)) {
  unlinkSync(join(outDir, stale));
  console.log(`removed stale ${stale}`);
}
console.log(
  `wrote ${written.length} vectors (${authored(THULLA_RULES_VERSION).length} authored, ${vectors.length - authored(THULLA_RULES_VERSION).length} endgames)`,
);
if (failed > 0) {
  console.error(`${failed} vectors failed against the engine; nothing was written for them`);
  process.exit(1);
}
