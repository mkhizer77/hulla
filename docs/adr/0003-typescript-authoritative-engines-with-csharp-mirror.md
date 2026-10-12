# ADR-0003: TypeScript rules engines are authoritative; C# mirrors pass shared vectors
Status: Accepted (C# mirror deferred past v1.0 by ADR-0019)
Date: 2026-10-11

## Context
The server runs TypeScript. The Unity client wants instant legal-move highlighting and, later, offline play, which needs the rules in C#. The web client will reuse the TypeScript engine. Two implementations of one rulebook risk divergence.

## Decision
The TypeScript engine in `packages/rules-engine` is the single authority at runtime. A C# mirror in `packages/rules-engine-cs` exists for client hints and future offline play. Both implement the GDD rule IDs, both must pass `packages/test-vectors`, and a nightly equivalence harness replays random rounds through both and compares state hashes. Rules are versioned and rounds pin their version.

## Consequences
Parity is enforced by tests rather than hope; the vector suite becomes the executable specification; web gets the engine for free. Cost: two implementations to maintain for every rule change, mitigated by small pure engines and generated test vectors.

## Alternatives considered
One engine compiled to both (WebAssembly in Unity or C# to JS): immature toolchains for Unity mobile and the Nakama runtime; server-only rules with no client engine: round-trip latency for highlighting and no offline path.
