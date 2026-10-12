# ADR-0022: Server-side validators are compiled ahead of time

Date: 13 October 2026. Status: accepted.

## Context
Every inbound payload is validated against the protocol schemas before any logic runs (ARCHITECTURE §8.4). The first generated validators were zod schemas, which the server bundled into Nakama's JavaScript runtime. The first scripted round against Nakama failed on the first validation call with "object is not extensible": Nakama freezes everything reachable from the global object once InitModule returns (`runtime.js_read_only_globals`, on by default), and zod 4 defines properties on schema instances that were created at load time. The bundle had also grown from 3 KB to 900 KB.

## Decision
The codegen emits two validator flavours from the same schemas: zod schemas for the web client, where a typed parse is worth the weight, and Ajv standalone code (`generated/ts/checks.ts`) for the server: plain functions compiled ahead of time with no class machinery and no schema compiler at runtime. The generated module exports a factory, `createChecks()`, because Ajv's functions write their `errors` property on every call; the server builds the checks where it uses them (per match core, per RPC call), so they are never reachable from the frozen globals. A payload that passes is cast to its generated type.

## Consequences
- The frozen global object stays on: it is what keeps the VM pool free of shared mutable state (ARCHITECTURE §8.1).
- The server bundle stays small and conservative (ES2015 syntax, no runtime reflection), which is what Nakama's engine wants (ARCHITECTURE §8.1).
- `@hulla/protocol` depends on `ajv` for the two runtime helpers the generated code imports; the compiler itself runs only in `tools/codegen`.
- A schema change regenerates both flavours; CI fails on drift as before. Nothing in `generated/` is hand-edited.
