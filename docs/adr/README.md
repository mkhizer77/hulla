# Architecture decision records

One file per decision, numbered, never edited after acceptance except to change status (Proposed → Accepted → Superseded by ADR-xxxx). Template at the end of this file.

| ADR | Title | Status |
|---|---|---|
| 0001 | Monorepo with pnpm workspaces and Unity as a subfolder | Accepted |
| 0002 | Self-hosted Nakama as the backend platform | Accepted |
| 0003 | TypeScript rules engines are authoritative; C# mirrors pass shared vectors | Accepted; mirror deferred by ADR-0019 |
| 0004 | Clean architecture layering enforced by tooling | Accepted |
| 0005 | JSON Schema is the protocol source of truth, with generated TypeScript and C# | Accepted; draft amended by ADR-0021 |
| 0006 | Event-sourced match log with snapshots and state hashes | Accepted |
| 0007 | UI Toolkit with runtime data binding for screens, world-space sprites for the table | Accepted |
| 0008 | Hand-wired composition root, Unity Awaitable and plain C# events | Accepted |
| 0009 | Deterministic engines with injected RNG and clock; commitment scheme for fair deals | Accepted; clock input superseded by ADR-0020 |
| 0010 | Bots and offline play deferred; deterministic auto-play at launch | Accepted |
| 0011 | Content is signed data delivered through a manifest and Addressables | Accepted |
| 0012 | Voice provider behind an interface, Agora first | Accepted |
| 0013 | Hosting and cost plan: free EU box for testing, paid regions with real users | Accepted |
| 0014 | PolyForm Noncommercial 1.0.0 licence for the public repository | Accepted |
| 0015 | CI and release policy: tests gate merges, tag-triggered Android builds | Accepted |
| 0016 | Unity 6000.4.9f1 pinned | Accepted |
| 0017 | Shared stack between Unity and web clients | Accepted |
| 0018 | Text chat, quick chat and emotes on Nakama channels and match data | Accepted |
| 0019 | C# engine mirror deferred past v1.0; the tutorial plays recorded event streams | Accepted |
| 0020 | Rules engines take no clock; the table core stamps time | Accepted |
| 0021 | Protocol schemas use JSON Schema draft-07 | Accepted |

## Template
```
# ADR-NNNN: Title
Status: Proposed | Accepted | Superseded by ADR-XXXX
Date: YYYY-MM-DD

## Context
## Decision
## Consequences
## Alternatives considered
```
