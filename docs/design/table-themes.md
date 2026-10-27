# Table themes

Seven looks for the whole app, picked in Settings → Table theme. Basant and Pop are in a playtest (`docs/design/look-playtest.md`). Chosen after two rounds of mockups and the visual scan in `docs/research/card-game-visual-scan.md`: game screens built as a scene, not app screens. Portrait only.

| Theme | Scene | Table | Motion |
|---|---|---|---|
| Club (default) | Navy room, damask wall, spotlight | Stitched black leather rail, gold inlay, green felt | The spotlight breathes; dust drifts in it |
| Haveli | Arches, frieze, tiled floor, lamplight | Carved wood, brass studs, maroon felt with a jaali | Lanterns sway and flicker; motes in the light |
| Rooftop | Night sky, city, water tank, roof | Truck-art rim, teal tin top with flowers | String bulbs glow unevenly and now and then dip; stars twinkle; steam off the chai |
| Mehndi | Magenta floor with marigold dots | Block-print rug with tassels and a medallion | Fairy lights twinkle; garlands sway; a petal falls now and then |
| Chai Adda | Wooden planks, warm lamp | Takht with brass corners, teal cloth, tea rings | The lamp breathes; steam off the chai |
| Basant | Blue afternoon sky, kites, rooftops with garlands and bunting; the table on a tiled terrace | A durrie: pink border, orange zigzag band, green field | Two kites rock and bob on their strings |
| Pop | Indigo to coral gradient with faint line mandalas | White rim around a violet felt | Soft glows breathe; a few motes drift |

## 1. How it is built
- `TableSkin` in the settings (Club by default) puts `skinned` and `skin--<id>` on the shell root (`ThemeClasses`).
- `Presentation/Theme/Skins.uss` holds the rules every theme shares (under `.skinned`) and each theme's tokens, display face, backdrop, table, buttons, panels, name plates, seat frame, card back and thulla stamp (under `.skin--<id>`). It loads after `Hulla.uss` in the runtime theme.
- Art comes from `tools/artkit/skins.py` (deterministic; `python3 tools/artkit/skins.py` with Pillow). Files live in `Presentation/Theme/Art/Skins/<id>/` and `.../shared/`.
- `AmbientLayer` is added by the shell behind every screen's content, plus the chai glasses on the rail for Rooftop and Adda.

## 2. Rules for the motion
- Slow and small: periods of 2.5 to 10 s, opacity swings under 0.1, sways of about a degree. Nothing that reads as an event.
- Flickers are rare: one dip of 80 to 160 ms every 6 to 16 s per bulb, a second dip a third of the time.
- Nothing takes a touch. Reduce motion freezes every layer.
- Deterministic for a seed so snapshots are stable.

## 3. Games on top of themes
A theme owns the scene, the rail and felt, seat frames, card backs, buttons and the thulla-moment stamp. A game owns what sits on the felt and the seat labels (Thulla: the led suit, voids, THULLA!; Rung: the trump badge, team trick counts, Court!). The themes do not change rules or information.

## 4. Fonts
Display faces per theme, all OFL and stored with their licences in `Presentation/Theme/Fonts`: Titan One (Club), Rozha One (Haveli titles; buttons stay in Nunito), Lilita One (Rooftop, Basant), Baloo Bhaijaan 2 ExtraBold (Mehndi), Baloo 2 ExtraBold (Adda, Pop). The two Baloo files are static ExtraBold instances cut from the variable fonts with fontTools.

Basant is the only light scene: its ink tokens are dark brown, so text reads on the sky and the cream panels. Pop keeps white ink on its gradient and sets dark ink inside its white cards (`--ink-ivory` is redefined on `.panel`, `.game` and the list rows).

## 5. Checking
`ScreenSnapshots` renders Home, the lobby, the table on your turn, a six-seat table, the thulla moment, the results and Settings once per theme into `HULLA_SNAPSHOT_DIR`.

## 6. Next
Vector nav icons, the multi-game Home shelf (Thulla, Rung soon), the results as a score sheet or podium per theme, and the art track's replacements for the generated placeholders.
