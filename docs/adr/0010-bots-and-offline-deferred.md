# ADR-0010: Bots and offline play deferred; deterministic auto-play at launch
Status: Accepted
Date: 2026-10-11

## Context
The owner decided the platform is friends first, random players optional, and AI or local play last and optional (GDD D7). Absences, timeouts and disconnects still need handling.

## Decision
Launch with the deterministic auto-play policy (GDD R44) covering timeouts and disconnects. Reserve the `botPolicy` hook in the module contract and an `IOfflineEngine`/`LocalMatchHost` seam behind `ITableSession` so bots and offline play can be added without redesign. Quick Play starts human-only tables at three or more players.

## Consequences
Smaller launch scope; honest tables; a liquidity risk in Quick Play that is mitigated by friend-first positioning and can be revisited with data. The bot design in GDD §8 stays documented.

## Alternatives considered
Bot backfill at launch (more work, fairness and labelling questions, contrary to D7); no auto-play (stalled tables).

## Update, 16 November 2026: bots are part of Rung
Rung is played in pairs, so an empty or absent seat costs a partner the game. The owner asked for bots that fill seats and play well enough to keep a bazi competitive. For Rung only:
- The host of a private room may put a bot in an empty seat (`add_bot`, `remove_bot`); a person who joins takes the seat back.
- A bot covers an absent seat (RG43) instead of the timeout policy.
- The bot samples the unseen cards, plays each sample out and scores by bazi points; it reads only its own hand and what the table has seen.

Thulla is unchanged: Quick Play bots for a lone player (29 October) and R44 for absences. Offline play stays deferred.

