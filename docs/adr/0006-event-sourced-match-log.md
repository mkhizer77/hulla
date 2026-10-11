# ADR-0006: Event-sourced match log with snapshots and state hashes
Status: Accepted
Date: 2026-10-11

## Context
Reconnection, dispute review, analytics, offline result verification and cross-engine parity all need a faithful record of what happened in a round.

## Decision
Every applied move appends events with a sequence number, server timestamp and a SHA-256 hash of the canonical full state. Snapshots are taken every 25 events and at phase boundaries. Logs are retained 30 days compressed; results forever.

## Consequences
Reconnection is snapshot plus tail; replays are exact; analytics derive from events rather than parallel tracking; the equivalence harness compares hashes. Storage cost is small for turn-based games.

## Alternatives considered
State-only persistence (no replay or dispute capability); client-side logs (not trustworthy).
