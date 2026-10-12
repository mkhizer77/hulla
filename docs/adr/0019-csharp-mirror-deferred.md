# ADR-0019: C# engine mirror deferred past v1.0; the tutorial plays recorded event streams
Status: Accepted
Date: 2026-10-12

## Context
ADR-0003 pairs the authoritative TypeScript engine with a C# mirror for client-side legality hints and later offline play. The audit of 11 October 2026 found that the server already sends each seat its legal mask in `turn_start` before the seat can act, so hints need no local engine, and that the mirror's only remaining v1.0 use was the tutorial. Offline play is itself deferred (ADR-0010). Two engines cost parity work on every rule change.

## Decision
`packages/rules-engine-cs` is not built for v1.0. The TypeScript engine is the only engine until offline play or another real client-side need is scheduled; vectors, traceability and CI target it alone, and the C# jobs stay in the workflows behind the existing folder detection. The tutorial is five pre-recorded event streams, produced from the TypeScript engine by `tools/vector-gen`, played through the normal `TableClient` and `AnimationScheduler` and branching on the player's tap, so it exercises the real table code and needs no engine on the device. Clients take legality from the server mask and assert nothing locally.

## Consequences
M0 shrinks to the TypeScript engine, vectors and traceability; no equivalence harness until the mirror exists; `Hulla.Domain` holds models and policies only. ADR-0003 stays accepted for the day the mirror is built, and the vectors are written so a second engine consumes them unchanged.

## Alternatives considered
Building the mirror now (parity cost for no launch feature); running the tutorial on the server (a network dependency in the first two minutes of the first session).
