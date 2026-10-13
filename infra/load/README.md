# Load test (k6)

k6 WebSocket scenarios for the server (GDD 28.1, 28.5): each virtual user runs one four-seat table end to end over Nakama's JSON socket protocol, and one seat per table drops and returns with its resume token. Thresholds: p95 move round-trip under 250 ms in region, more than 99% of checks passing, under 1% of HTTP requests failing.

## Run
```
brew install k6
k6 run infra/load/tables.js -e HULLA_HOST=127.0.0.1 -e HULLA_PORT=7350 -e TABLES=2 -e ROUNDS=1
```
`TABLES` is the number of concurrent tables (four players each; 250 tables is 1,000 concurrent players), `RAMP` and `HOLD` set the ramp and plateau (`1m`, `2m` by default), `ROUNDS` the rounds per table before it closes. For the phase 0 box: `-e HULLA_HOST=<host> -e HULLA_TLS=true -e HULLA_SERVER_KEY=<key>` and run from the player region to measure the in-region round trip.

## Chaos
Run the same scenarios with the network impaired between k6 and the server (GDD 28.1: 800 ms latency, 10% drops, reordering). On the box, `tc qdisc add dev eth0 root netem delay 800ms 100ms loss 10% reorder 25% 50%` for the test and `tc qdisc del dev eth0 root` after; on a Mac, a Toxiproxy in front of port 7350 with `latency` (800 ms, jitter 100) and `timeout`/`slicer` toxics. Afterwards `admin_round_replay` every round from the run and expect every hash to agree: zero divergent rounds is the gate.

## Results
`docs/research/load-test.md` records each run: date, host, tables, rounds, the p95 round trip, the error rate, reconnects and what broke.
