# Observability

Metrics, logs and alerts for the server (GDD 20.13, ARCHITECTURE 8.12, 12.6).

## Metrics
Nakama serves Prometheus metrics on port 9100 (`metrics.prometheus_port` in `server/nakama/config/local.yml`). Its own metrics cover sessions, API latency and errors per method; the runtime adds the game counters, exposed with Nakama's custom prefix as `nakama_custom_hulla_rounds_started`, `..._rounds_ended`, `..._moves` (tag `auto`), `..._timeouts`, `..._thullas`, `..._disconnects` (tag `outcome`), `..._reconnects`, `..._messages_dropped`, `..._tables_created`, and the summary `nakama_custom_hulla_round_duration` (seconds, quantiles 0.5 to 0.999), all tagged with `game` and `node_name`. Nakama's own `nakama_Rpc_count`, `nakama_Rpc_errors`, `nakama_Rpc_latency_ms` (a summary) and `nakama_sessions` cover the RPCs and sessions.

Locally: `docker compose -f server/nakama/compose.yml -f infra/observability/compose.yml up -d` runs Prometheus (http://127.0.0.1:9090) and Grafana (http://127.0.0.1:3000, admin / admin) with the "Hulla server" dashboard provisioned from `infra/observability/grafana/dashboards/hulla-server.json`. On the phase 0 box the same files feed Grafana Cloud's free tier (TECH_STACK 6): point its Prometheus remote-write or agent at port 9100 and import the dashboard JSON.

## Logs
Every log line is structured JSON with fields as keys (`server/nakama/src/infra/log.ts`): the message, then `tableId`, `roundId`, `seat`, `userId` where relevant. Names, handles, chat text, hands, cards, tokens and addresses are never logged; a field with such a key is written as `[redacted]`, and a unit test fails if a call site tries. Analytics events ride the same stream under `msg: "analytics"` (ARCHITECTURE 12.4).

## Alerts
`infra/observability/alerts.yml` holds the Prometheus rules: messages dropped inside a match handler, more than 1% of rounds with a server-side error, tables without rounds, disconnects above 5% of moves, and Nakama down. Delivery to the owner's phone is Grafana Cloud's contact point or UptimeRobot (owner setup, OQ-027). To test a rule, run the compose stack, start a round, then stop Postgres: `nakama_custom_hulla_messages_dropped` rises and `HullaMessagesDropped` fires within two minutes.
