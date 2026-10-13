# Load tests

Method: `infra/load/tables.js` (k6) runs one four-seat table per virtual user against a server and measures the move round trip (`move_played` sent to `move_applied` received), rounds completed, reconnects and errors. Gates (GDD 28.5): 1,000 concurrent players with p95 round trip under 250 ms in region; a chaos run (800 ms latency, 10% drops, reordering) with zero divergent rounds, checked with `admin_round_replay`.

| Date | Host | Tables (players) | Rounds | p95 round trip | Errors | Reconnects | Notes |
|---|---|---|---|---|---|---|---|
| pending | phase 0 box (#76) | 250 (1,000) | 3 | | | | The scenario is written and parses; k6 does not install on the dev Mac (Homebrew under Rosetta), so the first run is on the box or from a machine with k6. |
