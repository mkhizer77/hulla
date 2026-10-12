# @hulla/shared-ts

The bottom of the TypeScript dependency graph: small, pure building blocks used by the rules engines, the Nakama runtime modules and the web client.

## What is here
- `Result<T, E>` with `ok`, `err`, `map`, `mapErr`, `andThen`, `unwrapOr`, `all`. Domain and application code return these instead of throwing (CODING_RULES §3.4).
- The error-code catalogue (`ErrorCodes`, `ErrorCode`, `RuleError`), grouped by area: `rules`, `table`, `auth`, `economy`, `content`, `network`. Every code maps to a localisation key through `localisationKey` (ARCHITECTURE §12.5).
- Ids: branded `PlayerId`, `TableId`, `RoundId`, the room-code alphabet without 0, O, 1 and I, `generateRoomCode` and `normaliseRoomCode`.
- `Xoshiro128StarStar`, the PRNG both engines share (ADR-0009), seeded from the first 16 bytes of SHA-256 over the round seed; `nextInt` is unbiased by rejection sampling; `shuffle` is Fisher–Yates from the top. The exact algorithms are pinned in `src/__tests__/fixtures/xoshiro128ss.json`, generated from the published C reference, so the C# mirror can match them byte for byte (ARCHITECTURE §7.5).
- `sha256`, `sha256Hex`, `utf8Encode`, `bytesToHex`, `hexToBytes`: pure implementations, because Nakama's JS runtime has neither `crypto` nor `TextEncoder`.
- `canonicalJson` and `stateHash`: sorted keys, no whitespace, integers only; the hash every engine event carries (ADR-0006).

## Commands
```
pnpm --filter @hulla/shared-ts test
pnpm --filter @hulla/shared-ts typecheck
pnpm lint        # from the root: typecheck, eslint, prettier, dependency-cruiser
```

## Boundaries
Depends on nothing. No Node built-ins, no npm runtime dependencies, no `Date`, no `Math.random`, no `console`; ESLint and dependency-cruiser enforce this for everything outside `__tests__`. Tests may use Node (`node:crypto`, `TextEncoder`) as an oracle.

## How to test
Vitest with fast-check. Known-answer tests pin the PRNG, SHA-256 and canonical JSON; property tests cover ranges, permutations, round trips and agreement with Node's own implementations.
