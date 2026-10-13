# Incident triage

What to do when an alert fires or players report that tables are broken (GDD 20.13, 22.7; #91).

1. **Confirm.** `hello` on the live port; the Grafana "Hulla server" dashboard; the alert's own panel. If `hello` fails, it is the box or the edge: Cloudflare status, `docker compose ps`, `docker compose logs --tail 200 nakama`.
2. **Classify.** Rounds failing (`HullaMessagesDropped`, `HullaRoundErrorRate`): read the `message dropped` log lines, they carry `tableId` and the error. No rounds starting (`HullaNoRounds`): check the drain flag (`admin_drain {}`) and the minimum client version (`config_get`). Disconnects (`HullaDisconnectsHigh`): usually the edge or the box's network, compare `nakama_sessions`. RPC errors (`HullaRpcErrors`): the `analytics` and `error` log lines name the RPC.
3. **Contain.** A broken feature behind a flag: `config_set` turns it off per region without a release. A bad build: roll back (`deploy-and-rollback.md`). A bad player: `admin_sanction`. A broken table: `admin_table_inspect` then `admin_round_replay` to see whether the log and the state agree.
4. **Communicate.** The support channel gets one line within fifteen minutes of a confirmed incident and one at the end; the store listing is not touched.
5. **Record.** A short entry in `docs/research/incidents.md`: when, what players saw, cause, fix, what changes. A test or an alert follows every incident that reached players.

Targets: acknowledge an alert within fifteen minutes in waking hours, within two hours otherwise; player-visible incidents get a fix or a rollback within an hour.
