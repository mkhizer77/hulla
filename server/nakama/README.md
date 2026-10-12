# @hulla/server

The Hulla runtime modules for Nakama (ADR-0002): RPCs, hooks, the table core and the game modules, written in TypeScript and bundled into one file that Nakama's embedded JS runtime loads.

## Layout
- `src/main.ts`: `InitModule`, the one function Nakama calls at start-up; everything registers there.
- `src/rpc/`: RPC handlers. `hello` is the smoke test every environment answers.
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
```

## Runtime constraints
No Node APIs, no timers, no async in match handlers; only `nk` performs IO; every inbound payload passes the generated zod validator before any logic runs (ARCHITECTURE 8.1, CODING_RULES 3.5).
