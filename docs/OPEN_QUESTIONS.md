# Open questions

Implementation sometimes meets a gap or an ambiguity in the GDD. Contributors record it here instead of guessing silently, implement the most conservative interpretation, and reference the entry from the code comment and the pull request.

| ID | Date | Area (GDD §, R-id) | Question | Interim choice made in code | Decided by owner | Decision |
|---|---|---|---|---|---|---|
| OQ-001 | 2026-10-11 | §6.7, R26, R30 | Under `openingThullaApplies` on or `thullaEndsTrick` off the discard can be empty when R26 fires; R30 only holds for the default toggles. What does the empty-hand leader do? | Draw one random card from the next clockwise active player's hand, as `emptyHandLeader = drawNextPlayer` does, because it mirrors the endgame rule R38 | | |
| OQ-002 | 2026-10-11 | §6.3, R9, R12, two decks | With two decks there are two Aces of Spades. Who opens, and what does the other holder play? | The seat dealt an Ace of Spades first in dealing order opens with it; the other holder still follows with a spade and may play their Ace | Owner, 12 Oct 2026 | One deck and 3–6 seats for v1.0 and v1.1; `deckCount` accepts only 1. Revisit the opener with the 7–8 seat work (v1.2). |
| OQ-003 | 2026-10-11 | §6.10, R44–R46, `turnTimer` off | With the timer off nothing triggers auto-play for a disconnected or absent seat, so a private room can stall forever | A seat under auto-play acts after a fixed 3 s delay whatever the timer setting; R45's escalation stays timeout-driven, so with the timer off only disconnects hand a seat to auto-play | | |
| OQ-004 | 2026-10-11 | §6.8, R32, R35, R43 | Is the hand-take window a separate pause before every lead (5 s added to every trick in the default private preset) or just availability during the leader's turn? | `leaderOnly`: no separate pause; take-hand is available during the leader's turn until the leader plays. `anyone`: the first 5 s of the leader's turn form the window and the leader's play closes it early | | |
| OQ-005 | 2026-10-11 | §7.2, R52 | A Quick Play rematch passes with three yes votes at a four-seat table. Continue with three or wait for backfill? | Continue at once with the players who voted yes; the empty seat stays empty for that round | | |
| OQ-006 | 2026-10-11 | §9.6 | The widening schedule (10 s, 15 s, 20 s) is a product promise the Nakama matchmaker only approximates through `matchmaker.interval_sec` and ticket resubmission | Treat the seconds as tuning targets; resubmit the ticket with a relaxed query at each step and let the matchmaker interval settle the start | | |

Rules: one row per question; never delete rows, mark them decided; the owner's decision is then reflected in the GDD and the row links to the change.
