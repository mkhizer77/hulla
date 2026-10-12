# @hulla/protocol

The wire protocol, schema first (ADR-0005). `schemas/` is the source of truth; `generated/` (TypeScript types with zod validators for the web and standalone checks for the server, C# DTOs) arrives with codegen and is never edited by hand.

## Layout
- `schemas/envelope.json`: every socket message is `{ v, type, tableId, seq, ts, payload }`.
- `schemas/common.json`: cards, seats, ids, timestamps, error codes, per-seat arrays, series state.
- `schemas/opcodes.json`: the registry of every message type in GDD §20.6 with its Nakama opcode, direction and redaction class: `public` (seats and spectators), `seat` (one seat only, such as `turn_start` with its legal mask and `private_draw`), `recipient` (built per recipient, such as `table_state`).
- `schemas/shell/`: table lifecycle, presence, chat, economy shapes and errors; game-agnostic.
- `schemas/games/thulla/`: moves, events, the view and the rules schema. `rules.json` must equal the engine's `THULLA_RULES_SCHEMA`; a test enforces it.
- `examples/`: Appendix D messages and more; every file validates against the envelope and its payload schema.

## Compatibility rule
The envelope's `v` is the protocol major. Within a major, changes are additive only: new optional fields, new message types with new opcodes, new enum members that every client treats as "unknown, ignore". A breaking change bumps the major, the server keeps serving the previous major for a two-major window, and the ADR index carries the migration note (CODING_RULES §7). Opcodes are stable; new types append to the registry.

## Commands
```
pnpm --filter @hulla/protocol test      # compiles every schema, checks the registry, validates every example
```

## Boundaries
Depends on nothing at runtime. The engine is a dev dependency only, for the rules-schema equality test.
