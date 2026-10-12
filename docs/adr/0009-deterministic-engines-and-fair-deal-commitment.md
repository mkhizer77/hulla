# ADR-0009: Deterministic engines with injected RNG and clock; commitment scheme for fair deals
Status: Accepted (clock input superseded by ADR-0020)
Date: 2026-10-11

## Context
Parity between two engines, exact replays, reproducible bugs and verifiable fairness all require that a round is a pure function of its seed and move log. The Nakama JS runtime has no cryptographic random API.

## Decision
Engines receive `Rng` and `Clock` as inputs and never touch global randomness or time. The table core derives a 256-bit seed from four `nk.uuidv4()` values (Go `crypto/rand` behind them), drives a small PRNG implemented identically in TypeScript and C# (xoshiro128** or PCG32), shuffles with Fisher–Yates, publishes `sha256(seed ‖ canonical deck order)` before the deal and reveals the seed at round end. All in-round randomness uses the same seeded PRNG.

## Consequences
Rounds are reproducible from seed plus log; fairness is verifiable by any client; the two engines can be compared by hash. The PRNG must be bit-identical in both languages, which the vectors check.

## Alternatives considered
`Math.random` (not reproducible, not cryptographic); a Go runtime module for randomness (more operational surface for little gain); no commitment (fairness unverifiable).
