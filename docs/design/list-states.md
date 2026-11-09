# Loading, empty and error states

Every list in the app has three states besides its rows (GDD 14.13, #82): loading as three pulsing bars, empty as one sentence with one action, and an error inline with Retry. Nothing shows a blank screen or a raw error. `ListState` in `Presentation/Screens` draws all three; `ListStatesTests` opens each list with the server down and with nothing in it and checks the words and the action.

## Errors

- A code with its own line in `content/strings` shows that line (`error.<code>`). Every server code a player can meet has one; `packages/shared-ts/src/__tests__/errors.test.ts` fails when a new code has none.
- A client code for a service that could not be reached (`*.unavailable`, `session.not_signed_in`) shows `error.offline`: "Can't connect right now. Check your internet and try again."
- Anything else shows `error.unknown` ("Something went wrong. Try again in a moment.") and writes the code to the log, never to the screen.

## Maintenance

The server's `maintenance` flag rests Play now and puts a line on Home. With `maintenance_until` (Unix milliseconds) the line gives the time it ends in the player's own time zone: "Hulla is having a short break until 14:30. The rulebook and your friends still work." Without it, or once that time has passed, it says "Tables open again soon." The rulebook needs no server, and the friends list keeps working because the server is up while it drains.

## Screen by screen

| Screen | Loading | Empty: line and action | Error |
|---|---|---|---|
| Home | Connecting… on the status line | No friends online: the party bar invites; the hero offers Play now | Status line, plain words; tapping again retries |
| Friends | Bars | "Nobody here yet. Share your code to add friends." · Share | Inline with Retry; a later failure keeps the list and shows the line above it |
| Find friends (contacts, Facebook) | "Looking for friends who play…" | "None of them play Hulla yet. Share your code to bring them in." · the buttons stay as the action | Line under the buttons |
| Profile | Fields fill when loaded | Never empty (your own card) | Inline where the stats go, with Retry |
| Missions | Bars | "New missions arrive at midnight." · Play now | Inline with Retry |
| Achievements | Bars | Never empty (all forty come from content) | Inline with Retry |
| Leaderboard | Bars | "No points yet. Win rounds to get on the board." · Play now | Inline with Retry |
| Ranked | Bars in the ladder | Top players: "Nobody has finished placement yet." · the Play ranked button on the card above | Inline with Retry |
| Shop | Bars | "Nothing here this week." · See other items (the first category with stock) | Inline with Retry; with items already shown, the line above them |
| Shop, Gems tab | Bars while the store connects | Not a phone: "Purchases open in the app on your phone" | "The store isn't reachable right now" · Retry |
| Pass | Bars | Between seasons: "No season is running right now. The pass opens with the next one." | Inline with Retry |
| Quick Play | The search itself | Not a list | Status line in plain words; Cancel goes back |
| Lobby and table | The table state | Empty seats invite (Copy code) | Status line in plain words; the reconnect pill while the socket is down |
| Rulebook | None (built in) | Never empty | Never fails (no server) |
| Phone sign-in | Connecting… | Not a list | Under the field in plain words |

Play now on an empty list starts four-seat Quick Play, and is hidden while the server has Quick Play off.
