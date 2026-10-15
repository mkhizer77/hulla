# Rooftop redesign

The owner's art pack (kept outside the repository) redesigns the app in the Rooftop look: carved wood, brass, truck-art frames and toy icons over the night city. It is the app's only look: the five switchable table themes, their picker in Settings and their art were removed on 15 Oct 2026.

## Art
- `tools/artkit/ambient.py` draws the three ambient sprites (star glow, steam, chai glass).
- `tools/artkit/rooftop_import.py <pack>/sprites` copies the pack's sprites once each into `Presentation/Theme/Art/Skins/Rooftop/` (Frames, Buttons, Chips, Avatars, Cards, FX, Props, Nav) and writes their import settings: no mipmaps, clamped, bilinear, alpha as transparency.
- What the importer changes, and why: highlights the extraction keyed out as see-through are filled; pale halos from the light extraction background become a soft dark shadow; the Casual/Ranked track has its words painted out and its star knob cut free (labels translate, the knob slides); a blank violet coin covers tables of 3, which the pack has no coin for.
- `btn_primary_pressed` is a plain gold bar, not a pressed copy of the floral `btn_primary_normal`, so it is the stretchable primary button everywhere and the floral one presses by moving down.
- The pack's two backdrops (the rooftop for menus, the walled roof for the table) carry their own string lights, so the drawn lights are off for Rooftop. The pack has no table felt yet; `tools/artkit/rooftop_table.py` draws a stand-in.

## Styles
- `Theme/Skins.uss` holds the scene's shared rules and `Theme/Rooftop.uss`, loaded after it, the look itself; both hang off the always-on `skinned` class on the shell root. Rooftop.uss has the handover's colour tokens, Lilita One for display text and Nunito Bold / ExtraBold for UI text (static instances cut from the variable Nunito, whose default weight is ExtraLight), the shared controls on the pack's sprites, the navigation dock, and the screens.
- Nine-slice borders are set in USS (`-unity-slice-*`) and measured on the exported files; UI Toolkit's `url()` loads a PNG as a texture, so borders set in the Sprite Editor would not apply.

## Screens
- Shop: this week's lead item in the truck-art arch with Buy; everything else as tiles two to a row with a rarity tag and Equip, the price, or Equipped.
- Season pass: a road that scrolls sideways, the free reward over the tier bar and the premium under it on a magenta band. The "unlock premium" button in the pack is left out while monetisation is on hold; the existing line about the premium pass stays.
- Profile: the face in the brass ring, the nameplate and handle, the friend-code ticket, four stats on wood (losses in red), the bio on parchment, and the edit fields under them.
- Friends: each row carries the face in a small brass ring with a green dot when online; declines, removals and blocks are red.
- Settings: every toggle is the carved switch (knob left off, right on), sliders use the star knob, theme tiles sit in wood with a gold rim on the chosen one.
- Dialogs: the pack ships pieces but no dialog layout, so the question sits in dark ink on the parchment with the gold button for yes and a plank for no.
- Tablet landscape: the tabs become a rail down the left edge (`size--expanded` with `shell--with-nav` on the shell root).
- The thulla moment follows the pack's 1,200 ms timeline: a 4 px vertical jolt, the stamp slamming down from 1.6 times its size at 120 ms, and pile cards 50 ms apart.

## Navigation
Home, Friends, Inventory, Settings. Inventory holds the shop, the season pass, missions, achievements and the leaderboard and carries the pass badge; Profile opens from the level shield on Home and from Friends.

## Open
- GDD 14.6 still says a 2 px shake and 70 ms between pile cards; the pack's 4 px and 50 ms are built. Update the GDD.
- The pack has a THULLA! stamp but no "OYE HOYE!" for a pile of five or more; the big pile shows the same stamp.
- The profile has no level, title or chosen avatar yet, so the dossier shows no shield or title ribbon.
- The potli shows a rupee sign; a neutral coin suits every market better.
- The 4, 5 and 6 coins have their numerals painted in; the numbers stay as labels for screen readers.
- The deck badge has an 8 painted in; the table shows the real count beside it.
