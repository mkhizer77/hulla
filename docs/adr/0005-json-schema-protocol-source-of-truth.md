# ADR-0005: JSON Schema is the protocol source of truth, with generated TypeScript and C#
Status: Accepted (schema draft amended by ADR-0021)
Date: 2026-10-11

## Context
Two client languages and one server must agree on every message. Hand-written DTOs drift; wire bugs are expensive to debug across devices.

## Decision
JSON Schema (draft 2020-12) files in `packages/protocol/schemas` define the envelope, shell messages, per-game moves, events, views and rules, and the analytics events. `pnpm codegen` generates TypeScript types plus zod validators and C# DTOs into committed folders; CI fails on drift. Additive-only changes within a major version; a two-major compatibility window on the server.

## Consequences
Server-side validation of every payload is automatic; clients cannot diverge silently; adding a message type is a schema edit plus codegen. JSON stays the wire format at launch; MessagePack can replace it behind the same DTOs.

## Alternatives considered
Protobuf (strong tooling, but awkward in the Nakama JS runtime and heavier in Unity); TypeSpec (less common, fewer generators); TypeScript types as the source with C# generated from them (one-directional and weaker validation).
