# Home after the concept art

The owner's concept art for Home (kept outside the repository) sets the look for Rooftop, now the default theme. Home is built from it in every theme; Rooftop draws it with painted pieces, the other themes with their own colours.

## Layout, top to bottom
- A name sign hanging on two ropes, the level shield under it (tap: Profile), the coin pouch with the balance beside it. String lights over the top.
- When it applies: Rejoin, or the friend's table to join or watch, as one board.
- The Thulla card: a truck-art frame with a crest, roses and two peacocks; THULLA and its line; a fan of cards; seat medallions 3 to 6 on the fan; Casual or Ranked. Ranked sends Play now to the Ranked screen.
- Play now on a plaque over the card's foot.
- Create a room and Join by code on hanging boards; the reward chest with its Daily Reward board (the chest glows and either one claims while today's reward waits).
- Friends online, the join panel and status under the fold.
- Bottom navigation: Home, Friends, Inventory, Settings. Inventory holds the shop, the season pass, missions, achievements and the leaderboard, and carries the pass badge. Settings opens the settings screen.

## Art
`tools/artkit/rooftop.py` draws every piece into `Presentation/Theme/Art/Skins/dhaba/` (Pillow, deterministic). No text is baked in, so labels stay in `content/strings`. Painted art replaces a file under the same name and size; the stylesheet does not change. The chest and the pouch are theme-neutral and show in every theme.

## Checks
- `AuditSnapshots.EveryScreen` renders every screen in the default theme; `HULLA_SNAPSHOT_SKIN=club` (or another theme) renders that one.
- Medallions and the shield keep a 132 px touch target (48 dp) while the medallion art draws at 104 px.

## Open
- Long translations: the Play now plaque has a fixed width; a language with a longer phrase needs a smaller font there.
- The rest of the screens keep the theme's current look until the next pass.
