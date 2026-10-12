# @hulla/server

The Hulla runtime modules for Nakama (ADR-0002): RPCs, hooks, the table core and the game modules, written in TypeScript and bundled into one file that Nakama's embedded JS runtime loads.

## Layout
- `src/main.ts`: `InitModule`, the one function Nakama calls at start-up; everything registers there.
- `src/rpc/`: RPC handlers. `hello` is the smoke test every environment answers; `table_create` makes a table match for the caller.
- `integration/`: scripted `nakama-js` clients against a live Nakama (`pnpm --filter @hulla/server test:integration` with `HULLA_NAKAMA_HOST` set); CI runs it in the compose job.
- `src/games/thulla/`: the Thulla host (`host.ts`: moves in, events out, the legal mask per turn, the seed reveal) and `match.ts`, the named match callbacks InitModule registers.
- `src/match/registry.ts`: game id to host and match module name.
- `src/match/`: the table core, the generic match handler behind every game (ARCHITECTURE 7.2, 8.3 to 8.6). `table_core.ts` owns the lifecycle (lobby, in round, round ended, closing), the message pipeline (envelope and schema validation, seat and phase checks, idempotency by seat and clientSeq), deadlines and the auto-play policy, the message log with sequence numbers and state hashes, snapshots, redaction dispatch, the rematch loop and series state. `round.ts` seeds and deals rounds, applies moves and fires deadlines; `lobby.ts` holds seat and host rules; `log.ts` and `view.ts` build the log and the per-viewer `table_state`. `nakama_match.ts` adapts the core to Nakama's match callbacks. A game plugs in through the `GameHost` interface in `types.ts`, which wraps its engine module with wire encoding; the core knows no game.
- `src/infra/nk.ts`: the narrow interface over `nk` and the logger that services depend on; `src/__tests__/fake-nk.ts` fakes it for unit tests.
- `scripts/build.mjs`: esbuild bundles `src/main.ts` into `build/index.js`, lowered to ES2015 for goja. The bundle is git-ignored and built in CI.
- `config/local.yml`: Nakama config for local development with placeholder secrets; deployed boxes replace them.
- `compose.yml`: Postgres and Nakama for local development, with the bundle and config mounted. Deployment overlays live in `infra/compose/`.
- `Dockerfile`: Nakama plus the bundle and configs; `server-image.yml` builds it on `main`.

## Commands
```
pnpm --filter @hulla/server build          # bundle to build/index.js
pnpm --filter @hulla/server test           # unit tests with the fake runtime
docker compose -f server/nakama/compose.yml up   # local Nakama on 127.0.0.1:7350, console on 7351
curl -s "http://127.0.0.1:7350/v2/rpc/hello?http_key=local-only-http-key"   # smoke test
HULLA_NAKAMA_HOST=127.0.0.1 pnpm --filter @hulla/server test:integration   # scripted round
```

## Match handler notes
- Nakama resolves RPC and match callbacks by their global function names, so every registered handler is a named top-level function and `registerMatch` receives identifiers, never literals.
- Only public messages advance the table sequence number; seat-only messages carry the current one and are logged against their seat, so every client sees a gap-free stream and a reconnect replays exactly what that seat may see.
- Nakama's `matchJoin` has no metadata, so `matchJoinAttempt` records an admission (seat or spectator) that `matchJoin` consumes.
- Round records (seed, players, moves, result, final state hash) go to the `round_logs` storage collection under the system user at round end and when a table closes mid-round.

## Runtime constraints
No Node APIs, no timers, no async in match handlers; only `nk` performs IO; every inbound payload passes the generated check before any logic runs (ARCHITECTURE 8.1, CODING_RULES 3.5).
