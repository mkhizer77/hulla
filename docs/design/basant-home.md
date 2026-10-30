# Basant daytime: the one look, and Home built to its concept

Decided 27 Oct 2026: Basant daytime (concept 2 of the 26 Oct Home concepts) is the app's only look. The theme picker and the look playtest are gone from the client. Screens are rebuilt to the concept one at a time, starting with Home.

## How a screen is built

- **Concept.** The concept render is the target. Its boxes are measured on the render scaled to 1080 wide.
- **Art.** The art is made per element, never cut out of the render: blank plates with a pressed state, art without words, the game titles as lettered art, and every other word as a text sprite per language.
- **Import.** `tools/artkit/basant_import.py` brings a delivered folder into `Presentation/Theme/Art/Basant` (`Home`, `Text/<lang>`, `Backdrops`) at twice the display size.
- **Text kinds:**
  - Runtime text (names, numbers, the table banner, the day) is a Label in Baloo 2 styled to the concept.
  - Fixed words are labels marked with `TextArt.Mark(label, key)`. Basant.uss shows the language's sprite for `lang--<locale>` on the shell root. A language without sprites shows the label's own localised text, so nothing is ever blank.
- **Stage.** Home is a 1080 x 1736 stage with every element placed at its concept box. It scales to the room above the dock and stays centred, so taller phones and tablets keep the concept's layout.
- **Plates.** Plates hold their art's corners. Where a box is wider than the art, the plate stretches through a horizontal slice of its plain middle.

## Home

| Part | Behaviour |
|---|---|
| Gear | Opens Settings. The server field for development builds now lives at the bottom of Settings |
| Profile pill | Avatar (opens Profile), name (a tap edits it), level on the star |
| Coin pill | The balance |
| Friends online | Up to four faces with a presence dot; Invite opens Friends |
| Table banner | A friend's table with seats open, and JOIN; the table you left takes its place while your seat is held |
| Thulla card | Seat size 3 to 6, Casual or Ranked (Ranked opens the Ranked screen and rests the seat medallions), PLAY NOW |
| Create Room, Join by Code | As before |
| Daily Reward | The day of the calendar; glows while the reward waits, and a tap claims it |
| Rung | Coming soon |

## Dock

The dock has four tabs: Home, Friends, Trophies and Shop (decided 15 Oct). Trophies holds Missions, Achievements, the Leaderboard and, until the Shop gets its Pass tab, the season pass. Profile opens from the avatar on Home.

## Taps checked against the direction (28 Oct)

`docs/design/design-direction.md` 5.1, row by row: the gear, Play now, Create Room, Join by Code, the banners, the seat coins, the Casual/Ranked switch, the daily reward and Rung were already as listed. Added: the name board and the level star open the profile (the board opens the name field only while no name exists), the coin plate opens the shop, a friend's face joins their table when it has a seat, watches it when it can only be watched, and opens Friends otherwise, and Invite shares the friend code as a referral link (Friends when the code is not known yet). Not built: the player sheet from a face (Favourite, Remove, Block, Report live on Friends) and the "Tell me when it's out" toggle on Rung.
