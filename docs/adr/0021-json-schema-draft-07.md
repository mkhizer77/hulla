# ADR-0021: Protocol schemas use JSON Schema draft-07
Status: Accepted
Date: 2026-10-12

## Context
ADR-0005 named draft 2020-12. The C# generators the pipeline relies on handle draft-07 well and 2020-12 only partly, which would push hand-written workarounds into generated code.

## Decision
Schemas are written in draft-07, pinned in the `$schema` of every file and checked by `pnpm codegen`. Features 2020-12 adds (`unevaluatedProperties`, `$dynamicRef`, `prefixItems`) are not used; tuples are expressed as objects.

## Consequences
Both generators run unmodified; a move to a newer draft is a bulk edit once the generators catch up. ADR-0005 stands with its draft amended.

## Alternatives considered
Draft 2020-12 with the one generator that supports it (fewer tools to choose from); TypeSpec (fewer generators still).
