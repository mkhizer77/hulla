# Observability

Metrics, logs, dashboards and alerts for the server (GDD 20.13, ARCHITECTURE 8.12, 12.6; #75). Everything is self-hosted and free: Prometheus, Alertmanager, the blackbox prober and Grafana run next to Nakama from `infra/observability/compose.yml`. Alerts reach the owner's phone through an ntfy topic.

## SLOs (GDD 20.13)
| SLO | Measured by | Alerts |
|---|---|---|
| 99.5% availability a month | The prober asks Nakama's `/healthcheck` every 15 s (`probe_success`); `hulla:availability:ratio_*` are the ratios | `NakamaUnreachable`, `HullaAvailabilityBurnFast`, `HullaAvailabilityBurnSlow`, `HullaAvailabilityBelowSlo` |
| p95 move round trip under 250 ms in region | `nakama_custom_hulla_move_latency`: the server's share, from Nakama receiving a player's move to the broadcast that answers it, wait for the tick included. The network leg is not seen by the server | `HullaMoveLatencyHigh` at 200 ms (OQ-047) |
| Under 1% of rounds with a server-side error | `nakama_custom_hulla_messages_dropped` (a match handler threw) over `..._rounds_started` | `HullaRoundErrorRate`, `HullaMessagesDropped` |

## Metrics
Nakama serves Prometheus metrics on port 9100 (`metrics.prometheus_port` in `server/nakama/config/local.yml`). The runtime adds the game counters with Nakama's custom prefix (`src/infra/metrics.ts`): `nakama_custom_hulla_rounds_started`, `..._rounds_ended`, `..._moves` (tag `auto`), `..._timeouts`, `..._thullas`, `..._disconnects` (tag `outcome`), `..._reconnects`, `..._messages_dropped`, `..._tables_created`, the phone-code and purchase counters, and two summaries in seconds with quantiles 0.5 to 0.999: `..._round_duration` and `..._move_latency`. All carry `game` and `node_name`. A counter appears only after its first increment. Nakama's own: `nakama_Rpc_count`, `nakama_Rpc_errors`, `nakama_Rpc_latency_ms` per `rpc_id`, `nakama_sessions`, `nakama_authoritative_matches`, `nakama_db_in_use_conns`, `nakama_db_max_open_conns`, `nakama_db_total_wait_time_nanos`.

## Logs
Every log line is structured JSON with fields as keys (`server/nakama/src/infra/log.ts`): the message, then `tableId`, `roundId`, `seat`, `userId` where relevant. Names, handles, chat text, hands, cards, tokens and addresses are never logged; a field with such a key, or a value that is a card, is written as `[redacted]`. `src/__tests__/log.test.ts` fails when a call site logs a hand, a card list, a name or chat text, or calls the logger directly instead of the helper. Analytics events ride the same stream under `msg: "analytics"` (ARCHITECTURE 12.4). On the box: `docker compose logs --since 1h nakama | grep '"level":"error"'`.

## Bring it up
Locally, next to the server stack:
```
docker compose -f server/nakama/compose.yml -f infra/observability/compose.yml up -d
```
Grafana: http://127.0.0.1:3000 (admin / admin, anonymous viewing on), folder Hulla, dashboard "Hulla server". Prometheus: http://127.0.0.1:9090 (Alerts tab). Alertmanager: http://127.0.0.1:9093. Every port is on loopback only.

On the phase 0 box (#76), the same command from the checkout, with these in `server/nakama/.env` on the box (compose reads `.env` from the first file's folder; it is git-ignored and never committed):

| Variable | What | Default |
|---|---|---|
| `HULLA_ALERT_WEBHOOK_URL` | Where alerts go: `https://ntfy.sh/<long random topic>?template=alertmanager` | unset: alerts show in Grafana and Alertmanager but reach no phone |
| `HULLA_GRAFANA_PASSWORD` | Grafana's admin password | `admin` |
| `HULLA_GRAFANA_ANONYMOUS` | `false` on the box | `true` |
| `HULLA_PROMETHEUS_RETENTION` | How long metrics are kept; at least 30 days for the monthly SLO | `35d` |
| `HULLA_PROMETHEUS_PORT`, `HULLA_ALERTMANAGER_PORT`, `HULLA_GRAFANA_PORT` | Loopback ports | 9090, 9093, 3000 |

Grafana on the box is reached through a tunnel, never an open port: `ssh -L 3000:127.0.0.1:3000 <box>`, then http://127.0.0.1:3000. Data lives in the `prometheus-data`, `alertmanager-data` and `grafana-data` volumes; the stack restarts with the box.

### The phone (owner, once)
1. Install the ntfy app (Android or iOS, free, no account).
2. Pick a long random topic name, for example `openssl rand -hex 16`; the name is the secret, anyone who knows it can read the alerts.
3. Subscribe to it in the app, set `HULLA_ALERT_WEBHOOK_URL=https://ntfy.sh/<topic>?template=alertmanager` on the box and run the compose command again.
4. Send a test alert: `docker compose -f server/nakama/compose.yml -f infra/observability/compose.yml exec alertmanager amtool alert add HullaDeliveryTest severity=warn --annotation='summary="Delivery test"' --alertmanager.url=http://127.0.0.1:9093`. The push arrives within a minute; it resolves by itself after five.

Pages (`severity: page`) repeat every four hours while firing, warnings every twelve. When Nakama is down, the `Hulla*` alerts that depend on its metrics are held back.

## What each alert means
| Alert | Severity | Means | First look |
|---|---|---|---|
| `NakamaUnreachable` | page | The healthcheck has failed for two minutes: nobody can play | `docker compose ps`, `docker compose logs --tail 200 nakama`, the box itself |
| `NakamaDown` | page | Prometheus cannot scrape port 9100 | As above; if the healthcheck still answers, only the metrics port is wrong |
| `HullaAvailabilityBurnFast` | page | Over 7% of healthchecks failed in the last hour, still failing: 2% of the month's budget gone in an hour | Incident triage, step 1 |
| `HullaAvailabilityBurnSlow` | warn | Over 3% failed over six hours: flapping or short outages | The healthcheck panel; restarts in `docker compose ps` |
| `HullaAvailabilityBelowSlo` | warn | The last 30 days are under 99.5% | Record it in the week review; no action at night |
| `HullaMessagesDropped` | page | A match handler throws on table messages | `message dropped` log lines with `tableId` and the error |
| `HullaRoundErrorRate` | page | More than 1% of rounds hit a server-side error in the last hour | As above, then `admin_round_replay` on one table |
| `HullaMoveLatencyHigh` | warn | p95 of a move inside the server is above 200 ms | CPU on the box, the database panel, the last deploy |
| `HullaDatabaseSaturated` | warn | Over 80% of Nakama's database connections are busy | Slow queries in Postgres, `database.max_open_conns` |
| `HullaDatabaseWaits` | warn | Calls wait for a free connection | As above |
| `HullaNoRounds` | warn | Tables are created but no round starts | The drain flag (`admin_drain {}`), the minimum client version |
| `HullaDisconnectsHigh` | warn | Disconnects above 5% of moves | The edge or the box's network; `nakama_sessions` |
| `HullaRpcErrors` | warn | More than 5% of RPC calls fail | `nakama_Rpc_errors` by `rpc_id`, the `error` log lines |
| `HullaPhoneCodeSpendHigh` | warn | Phone codes cost over US$20 in an hour | `phone-sign-in-setup.md` |
| `HullaPhoneCodesUnanswered` | page | Most phone codes go unanswered: likely SMS pumping | Set `otp_daily_budget` to 0, then check the countries |

Thresholds start strict; `first-week.md` says how to tune them on real traffic.

## Checks and drills
`pnpm observability:check` (Docker needed, also run in CI) checks the Prometheus config, the rules, the Alertmanager config, the dashboard JSON and the compose file, and runs `infra/observability/alerts.test.yml`: induced dropped messages fire `HullaRoundErrorRate` and `HullaMessagesDropped`, a failing healthcheck fires `NakamaUnreachable` and `HullaAvailabilityBurnFast`, slow moves fire `HullaMoveLatencyHigh`, a busy pool fires `HullaDatabaseSaturated`, and healthy traffic fires nothing. CI also starts Prometheus next to the smoke-test server and checks that the scripted round's counters arrive.

On the box, before launch (owner, #76):
1. Live traffic: play a round from two phones; "Rounds started per minute" and "Moves per minute" move within a minute.
2. Delivery: the test alert above reaches the phone.
3. Induced outage: `docker compose stop nakama` for three minutes; `NakamaUnreachable` reaches the phone, then the resolved push after `docker compose start nakama`. Do it outside play hours.

| Date | Live traffic seen | Test alert on the phone | Outage alert and resolve | Notes |
|---|---|---|---|---|
| pending | | | | First run on the phase 0 box (#76). |
