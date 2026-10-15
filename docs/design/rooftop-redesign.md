# Rooftop redesign

The owner's art pack (kept outside the repository) redesigns the app in the Rooftop look: carved wood, brass, truck-art frames and toy icons over the night city. Rooftop is the default theme; the other four themes keep working with the same layouts in their own colours.

## Art
- `tools/artkit/rooftop_import.py <pack>/sprites` copies the pack's sprites once each into `Presentation/Theme/Art/Skins/Rooftop/` (Frames, Buttons, Chips, Avatars, Cards, FX, Props, Nav) and writes their import settings: no mipmaps, clamped, bilinear, alpha as transparency.
- What the importer changes, and why: highlights the extraction keyed out as see-through are filled; pale halos from the light extraction background become a soft dark shadow; the Casual/Ranked track has its words painted out and its star knob cut free (labels translate, the knob slides); a blank violet coin covers tables of 3, which the pack has no coin for.
- `btn_primary_pressed` is a plain gold bar, not a pressed copy of the floral `btn_primary_normal`, so it is the stretchable primary button everywhere and the floral one presses by moving down.
- The pack has no sky or table felt yet; the night scene and the felt stay on the earlier Rooftop art.

## Styles
- `Theme/Rooftop.uss`, loaded after `Skins.uss`: the handover's colour tokens, Lilita One for display text and Nunito Bold / ExtraBold for UI text (static instances cut from the variable Nunito, whose default weight is ExtraLight), the shared controls on the pack's sprites, the navigation dock, and the screens.
- Nine-slice borders are set in USS (`-unity-slice-*`) and measured on the exported files; UI Toolkit's `url()` loads a PNG as a texture, so borders set in the Sprite Editor would not apply.

## Navigation
Home, Friends, Inventory, Settings. Inventory holds the shop, the season pass, missions, achievements and the leaderboard and carries the pass badge; Profile opens from the level shield on Home and from Friends.

## Open
- The potli shows a rupee sign; a neutral coin suits every market better.
- The 4, 5 and 6 coins have their numerals painted in; the numbers stay as labels for screen readers.
- The deck badge has an 8 painted in; the table shows the real count beside it.
