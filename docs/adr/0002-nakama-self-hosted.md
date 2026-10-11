# ADR-0002: Self-hosted Nakama as the backend platform
Status: Accepted
Date: 2026-10-11

## Context
The platform needs authoritative hidden-information matches, tables with codes, matchmaking, friends, groups, chat, presence, leaderboards, wallets, receipt validation and push, for Unity now and a TypeScript web client later, at low fixed cost and with one-person operations. Unity's own dedicated hosting was reportedly wound down in early 2026.

## Decision
Nakama (Apache-2.0), self-hosted on one node with managed Postgres, with our game logic in TypeScript runtime modules. Open-source Unity and JavaScript SDKs cover both clients.

## Consequences
Most social and account features come built in; the authoritative match handler is ours and small; vertical scaling first, with regional deployments or Heroic Cloud as the growth path. We accept the JS runtime constraints (no Node APIs, synchronous handlers) and keep the rules engine pure accordingly.

## Alternatives considered
Colyseus (good rooms, but accounts, friends, leaderboards and groups would be ours to build); Photon Fusion or Quantum (built for real-time action, awkward for hidden-information turn games, CCU pricing); PlayFab plus a custom real-time server (two systems to operate); Firebase with Cloud Functions (poor fit for live tables); a custom Go or Node server (most work, most risk).
