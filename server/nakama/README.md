# @hulla/server

The Hulla runtime modules for Nakama (ADR-0002): RPCs, hooks, the table core and the game modules, written in TypeScript and bundled into one file that Nakama's embedded JS runtime loads.

## Layout
- `src/main.ts`: `InitModule`, the one function Nakama calls at start-up; everything registers there.
- `src/rpc/`: RPC handlers. `hello` is the smoke test every environment answers; `table_create` makes a table match and registers its room code; `table_join_by_code` resolves a code to a match; `presets_get` serves the presets bundled from `content/presets`; `voice_token` mints an Agora channel token for a presence at the table.
- `src/services/`: `table_registry.ts` (room codes in the `table_codes` storage collection with region and expiry), `agora_token.ts` (the AccessToken2 builder, written here because the runtime has no zlib; its test checks it against Agora's reference builder) and `bytes.ts` (HMAC, a stored-block zlib stream, base64).
- `src/content/`: content files bundled at build time; `pnpm content:validate` checks them against their schemas and the engine.
- `integration/`: scripted `nakama-js` clients against a live Nakama (`pnpm --filter @hulla/server test:integration` with `HULLA_NAKAMA_HOST` set); CI runs it in the compose job.
- `src/games/thulla/`: the Thulla host (`host.ts`: moves in, events out, the legal mask per turn, the seed reveal) and `match.ts`, the named match callbacks InitModule registers.
- `src/match/registry.ts`: game id to host and match module name.
- `src/hooks/before_channel_message.ts`: table text chat rides a Nakama room channel named after the match id (ADR-0018); before a message is delivered the hook enforces 140 characters, no links or phone numbers, one message per 2 s and ten per minute per account, with timestamps in the `chat_rate` collection. A refusal is a thrown error, which Nakama logs at error level and returns to the sender. Quick chat and emotes are match data: the core checks the phrase or emote id against the bundled packs (`content/quick-chat`, `content/emotes`), applies a 3 s cooldown per sender and broadcasts `quick_chat_sent` or `emote_sent` with the sender's seat; spectators may not send them.
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
HULLA_BIND=0.0.0.0 docker compose -f server/nakama/compose.yml up   # to play from phones on your LAN
curl -s "http://127.0.0.1:7350/v2/rpc/hello?http_key=local-only-http-key"   # smoke test
HULLA_NAKAMA_HOST=127.0.0.1 pnpm --filter @hulla/server test:integration   # scripted round
```

## Match handler notes
- Nakama freezes everything reachable from the global object after InitModule and rebuilds the match state as plain data between callbacks: validators are created per call (`createChecks()`), the state holds records and the PRNG words, never Maps or instances (ADR-0022).
- Voice is off until the box sets `AGORA_APP_ID` and `AGORA_APP_CERTIFICATE` in `runtime.env`; `voice_token` then answers `voice.unavailable`.
- Nakama resolves RPC and match callbacks by their global function names, so every registered handler is a named top-level function and `registerMatch` receives identifiers, never literals.
- Only public messages advance the table sequence number; seat-only messages carry the current one and are logged against their seat, so every client sees a gap-free stream and a reconnect replays exactly what that seat may see.
- Nakama's `matchJoin` has no metadata, so `matchJoinAttempt` records an admission (seat or spectator) that `matchJoin` consumes.
- Round records (seed, players, moves, result, final state hash) go to the `round_logs` storage collection under the system user at round end and when a table closes mid-round.

## Runtime constraints
No Node APIs, no timers, no async in match handlers; only `nk` performs IO; every inbound payload passes the generated check before any logic runs (ARCHITECTURE 8.1, CODING_RULES 3.5).
