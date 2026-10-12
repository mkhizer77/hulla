# @hulla/rules-engine

The authoritative rules engines (ADR-0003). Pure and deterministic: no IO, no clock (ADR-0020), randomness only through the injected `Rng` (ADR-0009), immutable state, `Result` instead of exceptions. The same code runs inside Nakama's JS runtime and in the browser.

## Layout
- `src/contract.ts`: the `GameModule` contract every game implements and the table core consumes (ARCHITECTURE §7.1).
- `src/card-kit/`: cards as two-character codes (`AS`, `TH`, `2C`), the canonical order used for hashing (♠ ♥ ♣ ♦, 2 low to A high), the deck, clockwise dealing and seat arithmetic.
- `src/thulla/`: the Thulla module. `rules.ts` holds the toggles of GDD §6.13 that change legality or outcomes, their defaults per mode, the presets and the JSON Schema; `state.ts` the immutable round state; `deal.ts` the seeded deal with the fair-deal commitment (R6).

## The fair deal
`createThullaRound` shuffles with the `Rng` the table core derived from the round seed, then publishes `commitment = SHA-256(seed ‖ canonicalJson(deckOrder))`. The seed stays server-only until the round ends; any client can then recompute the hash from the revealed seed and the dealt deck.

## Commands
```
pnpm --filter @hulla/rules-engine test
pnpm --filter @hulla/rules-engine typecheck
pnpm lint
```

## Boundaries
Depends on `@hulla/shared-ts` and nothing else. No Node built-ins, no npm runtime packages, no `Date`, no `Math.random`; ESLint and dependency-cruiser enforce it outside `__tests__`. Tests name the GDD rule IDs they cover.
