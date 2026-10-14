# Deploy and rollback

Blue-green deploys of the Nakama node with running rounds draining first, migrations gated by the smoke test, and a rollback that is one switch away (GDD 20.15, ARCHITECTURE 15, #90).

## Before
1. The tag: `vX.Y.Z` on `main` with CI green. The release workflow builds the Android artefacts; the server image is the same bundle (`server/nakama/build/index.js`) with the config under `server/nakama/config`.
2. The database: take the point-in-time marker (`docs/runbooks/database-restore.md`, step 1) so a rollback of a migration is possible.
3. Sign the content manifest (ADR-0011) when `content/` changed: `pnpm content:sign /path/to/content-signing.pem` prints the signature; set it as `HULLA_CONTENT_SIGNATURE` in the green node's runtime env, with `HULLA_CONTENT_SIGNING_KEY_ID` naming the key. The private key never goes on the box. Without a signature, release clients keep the content they shipped with. The key is made once, before the first release: `openssl genrsa -traditional -out content-signing.pem 3072`, kept with the other owner secrets, and its public half (`openssl rsa -in content-signing.pem -pubout`) pasted into `apps/unity/Hulla/Assets/_Project/Composition/ContentKey.cs`.
4. Tell the players if the window is long: the `maintenance` flag in `config_set` shows the line in the client; the drain alone shows "the table is full for a moment, try again".

## Deploy (blue-green on one box)
1. Start the new container next to the old one on another port (`green`), same Postgres: `NAKAMA_IMAGE=... docker compose -p green -f server/nakama/compose.yml up -d nakama` with `socket.port 7352` in its config. `migrate up` runs in its entrypoint; it must finish and `hello` must answer on 7352 (`curl ".../v2/rpc/hello?http_key=..."` shows the new `server` version) before anything else. A failed migration stops here: the old node is untouched.
4. Drain the old node: `admin_drain {"on": true, "reason": "deploy vX.Y.Z"}` with the server key. From then on it refuses `table_create` and matchmaker tickets with `table.draining`; tables already running play to their end and the players at them see nothing.
5. Switch the edge: point the Cloudflare origin (or the tunnel) at the green port. New sessions land on the new node.
6. Wait for the old node to empty: `admin_drain {}` reports `liveTables`; `nakama_sessions` on its metrics port falls to zero. Private tables can linger (an open session keeps a table for thirty minutes without players); when `liveTables` is zero, stop the old container.
7. Record the deploy in `docs/research/deploys.md`: version, time, how long the drain took.

## Rollback
1. If the new node misbehaves before the old one is stopped: switch the edge back, `admin_drain {"on": false}` on the old node, stop the green container. Nothing else changes.
2. After the old node is gone: start a container with the previous image and bundle on the blue port, migrate is a no-op or forward-only (every migration must be backward compatible for one version, which is why schema changes are additive), switch the edge, drain the new node the same way.
3. If a migration must be undone, restore the database to the marker from "Before" (`database-restore.md`); every round started after the marker is lost, so prefer forward fixes.

## Checks every time
- `hello` on the live port answers with the expected `server`, `rulesVersion` and `draining: false`.
- The dashboard shows rounds starting again within five minutes; `HullaNoRounds` must not fire.
- The scripted round (`pnpm --filter @hulla/server test:integration` against the box with the test server key) passes.

## Drill record
| Date | Version | Drain took | Rollback exercised | Notes |
|---|---|---|---|---|
| pending | | | | First rehearsal on the phase 0 box (#76). |
