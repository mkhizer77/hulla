# ADR-0020: Rules engines take no clock; the table core stamps time
Status: Accepted
Date: 2026-10-12

## Context
ADR-0009 injected both `Rng` and `Clock` into the engines. A clock input invites time-dependent rules and means a replay must reproduce every clock reading, not only the move log.

## Decision
`applyMove` and `onDeadline` take no clock. Deadlines, timeouts and timestamps belong to the table core, which records them on events as it logs them. Engine output is a pure function of state, move and `Rng`. ADR-0009's determinism, fair-deal commitment and PRNG rules are unchanged.

## Consequences
A replay needs only the seed and the move log; the ESLint ban on `Date` in engine packages stands; the table core's own time handling is tested with a fake clock.

## Alternatives considered
Keeping the clock and limiting it to stamping events, a rule only review could enforce.
