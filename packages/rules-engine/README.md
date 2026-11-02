# @hulla/rules-engine

The authoritative rules engines (ADR-0003). Pure and deterministic: no IO, no clock (ADR-0020), randomness only through the injected `Rng` (ADR-0009), immutable state, `Result` instead of exceptions. The same code runs inside Nakama's JS runtime and in the browser.

## Layout
- `src/contract.ts`: the `GameModule` contract every game implements and the table core consumes (ARCHITECTURE §7.1).
- `src/card-kit/`: cards as two-character codes (`AS`, `TH`, `2C`), the canonical order used for hashing (♠ ♥ ♣ ♦, 2 low to A high), the deck, clockwise dealing and seat arithmetic.
- `src/thulla/`: the Thulla module. `rules.ts` holds the toggles of GDD §6.13 that change legality or outcomes, their defaults per mode, the presets and the JSON Schema; `state.ts` the immutable round state; `deal.ts` the seeded deal with the fair-deal commitment (R6); `engine.ts` `legalMoves` and the total `applyMove` that share one validation path; `trick.ts` trick resolution following GDD §6.14 (pick-up or discard, escapes before any draw, the empty-hand draws, the endgame draw from the opponent); `hand-take.ts` R31 to R36; `events.ts` the event union clients render, with `private_draw` naming who may see the card.

## The endgame and the forced outcome
With two players left, a resolution that empties the leader's hand plans a draw (`pendingDraw`) rather than executing it. `applyMove` then runs the R41 search on that state, enumerating every legal choice of both players and every card the draw could produce, memoised over canonical state and capped at 50,000 nodes; only afterwards does it execute the draw with the `Rng`. When every continuation names the same Bhabhi the engine emits `confirmed`, and with `confirmedFastForward` on it plays the rest of the round out with the R44 auto-play policy in the same call. A state handed to the table core never carries a pending draw.

## Views, results and the module
`redact` builds the view for a seat or a spectator: own hand, counts for everyone else, the public pile, a discard count, and the seed and deck order only once the round has ended (R6). `redactEvent` drops a `private_draw` for anyone it does not name. `results` yields the escape order, the Bhabhi, the Baithak points of R50 and the per-seat stats; `seriesUpdate` applies the R51 formats and rotates the dealer. `thullaModule` bundles all of it as the `GameModule` the table core registers, with `onDeadline` on the R44 policy.

## Moves and events
A seat sends `play_card` or `take_hand`. A move is legal exactly when `applyMove` accepts it; a property test replays random rounds under every toggle and checks that for every seat and every card at every step. Failures are `RuleError`s from the catalogue, never throws (R47). Each accepted move yields events in order: `card_played`, then on resolution `trick_resolved`, any `escape`, a `private_draw`, `endgame_started` and finally `round_ended`.

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

## Bots
Bots (GDD 8) live in `thulla/bot.ts` and `thulla/bot-memory.ts`, apart from the R44 policy, which still covers timeouts and absent players. The memory folds the public events of a round: the suits each seat showed it lacks and the cards each seat picked up. For a decision the bot deals the unseen cards many times in ways that fit that memory and the hand counts, plays the rest of the trick out with a plain human reply for the others, and keeps the card that leaves it with the fewest and lowest cards without picking up. With two players left and twelve or fewer cards it searches the endgame exactly (600 positions at most). `REGULAR_BOT` holds the weights; a close second-best card is played now and then so it does not feel like a machine. In self-play one bot against three seats on the R44 policy is the Bhabhi in about 2% of rounds; in Nakama's runtime a decision takes about 18 ms on average and at most about 130 ms.
