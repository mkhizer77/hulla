# ADR-0004: Clean architecture layering enforced by tooling
Status: Accepted
Date: 2026-10-11

## Context
With a small team moving fast, conventions that live only in documents erode. Layer violations are the most common and most expensive drift.

## Decision
Domain → Application → Infrastructure/Presentation with the dependency rule, enforced mechanically: Unity assembly definitions with explicit references and `noEngineReferences` on the domain, NetArchTest assertions, dependency-cruiser and ESLint restricted imports for TypeScript, CI gates on all of them. SOLID is applied as described in `docs/ARCHITECTURE.md` §2.2 and the module contract.

## Consequences
Violations fail CI rather than surfacing in review; the domain stays testable without Unity or Nakama; some boilerplate (interfaces and adapters) is accepted as the price of substitutability.

## Alternatives considered
Convention only (rejected: it erodes); a single assembly for speed (rejected: untestable domain, slow compile times in Unity).
