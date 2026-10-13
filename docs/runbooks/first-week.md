# The first week live

Alert thresholds for real traffic, the hotfix path, the scale check and the daily ritual (GDD 20.12, 20.14, 29.2; #97).

## Daily ritual (20 minutes, same time every day)
1. The "Hulla server" dashboard: rounds started, moves, timeouts, disconnects, RPC errors and latency, sessions. Note the peak CCU.
2. Alerts that fired and what was done (`docs/research/incidents.md`).
3. Reports queue (`reports_list`) and the audit log.
4. Store reviews and the support inbox: themes, not every message.
5. The economy: coins earned versus spent, cap hits, pass tier distribution (from the `analytics` log lines).
6. One decision for the day: a flag to flip, a threshold to move, a hotfix to ship, or nothing.

## Alert thresholds
The rules in `infra/observability/alerts.yml` start strict (messages dropped at all, 1% of rounds with an error, 5% disconnects). After three days of real traffic, set each threshold at twice the observed p95 of the healthy days and record the change in the review.

## Hotfix path
1. Branch `hotfix/<short-name>` from the released tag.
2. Fix with a test; `pnpm lint`, `pnpm test`, the scripted round against the box.
3. Pull request to `main`, CI green, squash merge (the tests gate never moves).
4. Tag `vX.Y.(Z+1)` on `main`; the release workflow builds; the store upload is manual (CONTRIBUTING, "Releasing"); the server deploys by `deploy-and-rollback.md`.
5. The drill before launch: ship a one-line change this way end to end and time it; the target is under four hours from branch to store submission.

## Scale check
Against GDD 20.14 and TECH_STACK 6: the phase 0 box must hold the observed peak CCU with half its CPU and memory free at the peak, Postgres connections under half the pool, p95 RPC latency under 100 ms in region. If any of those fails, move to the next box size before the next weekend; a single node carries several thousand turn-based players (GDD 20.12), so size, not architecture, is the lever in week one.

## The review
At the end of the week, `docs/research/week-one-review.md`: traffic, incidents, what the alerts caught and missed, the economy numbers against the projections of GDD 13.8, the scale decision, and the three changes for week two.
