# ADR-0001: Monorepo with pnpm workspaces and Unity as a subfolder
Status: Accepted
Date: 2026-10-11

## Context
One developer will build a server, a Unity client, a web client and shared packages. Shared vectors, protocol schemas and content must change atomically with the code that uses them. Unity projects are large and have their own tooling.

## Decision
One repository. pnpm workspaces manage the TypeScript packages (`packages/*`, `server/nakama`, `apps/web`). The Unity project lives at `apps/unity/Hulla` and consumes `packages/rules-engine-cs` as a local UPM package and the generated C# protocol as source. One CI pipeline runs TypeScript, C# and content checks; Unity builds run in their own workflow.

## Consequences
Atomic changes across engine, vectors, schema and clients; one place to read the rules; a larger clone (Unity assets use Git LFS). Unity's `Library/` and build outputs are ignored; `.meta` files are committed.

## Alternatives considered
Separate repositories per client and server (rejected: schema and vector drift, three places to keep in sync); a Unity-only repository with the server as a submodule (rejected: submodules are error-prone).
