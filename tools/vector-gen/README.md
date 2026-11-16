# @hulla/vector-gen

Writes `packages/test-vectors/thulla/V-*.json`. `src/authored.ts` holds the hand-written scenarios, one or more per rule or edge id, with assertions derived from the GDD by hand; `src/endgames.ts` enumerates small two-player endgames and records what the forced-outcome search decides for each. `generate.ts` runs every vector through the TypeScript engine first, refuses to write anything that fails, fills in the deal commitment and the final state hash, and deletes stale files.

`src/rung-authored.ts` holds the Rung scenarios, written to `packages/test-vectors/rung/V-R*.json` the same way.

```
pnpm --filter @hulla/vector-gen generate
pnpm vectors
```
