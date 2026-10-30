# Basant: the lobby

Built 28 Oct 2026 to the approved concept (the Lobby render of 27 Oct). It follows the same method as Home (`basant-home.md`): a 1080 x 1936 stage scaled to fit the whole screen, each element at its concept box, art per element, words drawn by Unity.

## Shared kit

The lobby introduces the kit every screen after Home draws from, under `Presentation/Theme/Art/Basant/Kit`: the calm sky (`Backdrops/calm.png`, with the far kites as separate layers), round, square, pill and teal buttons with pressed states, fifteen line icons (book, more, close, back, tick, pencil, plus, share, copy, mic, mic off, chat, search, eye, refresh), the name plate, three status tags, the label plate, the cream panel with the block-print border, the empty seat, the host's crown and the ready tick. All are blank; `tools/artkit/basant_import.py` places a kit file by its stem (`KIT_WIDTH`) and a screen's own piece by `SCREEN_WIDTH`.

## Parts

| Part | Behaviour |
|---|---|
| Leave, Private table, book | Leave is the one worded top-bar action; the book opens the rulebook |
| Room code card | The code in yellow lettering; Share (teal, the share icon) and copy (the square, shows a tick for two seconds). A Quick Play table shows "Quick Play table" and no sharing |
| Rug | `Lobby/rug-table.png`, shared later with the table, the tutorial and watching |
| Seats | `LobbySeat`: the player's own seat at the bottom in the gold rim, the rest clockwise on a rounded square so six reach the rug's corners. Name plate under each; a tag for Host, Ready or Not ready; the crown on the host's rim, the tick on a ready player's. An empty seat is the dashed circle with Invite and opens the invite sheet (or Share) on a tap |
| Rules line | The rules summary under the rug; the host's pencil opens the editor |
| Chat strip | The latest table line on the cream pill; the mic (shown once voice is wired for the lobby) and the chat bubble, which opens the compose sheet with the last five lines |
| READY / START | One yellow button: READY (or NOT READY) for a player, START for a host who is ready, enabled with three or more seated |
| Hint | "<host> starts when 3 or more are ready" for players, "Start when 3 or more are seated" for the host, or the error or watching note |

The `LobbyAtConceptSize` snapshot (1080 x 1936) lays the screen over the concept.
