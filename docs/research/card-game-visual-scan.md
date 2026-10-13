# Card game visual scan (October 2026)

How the card games played across South Asia look at the table, read from the current Play Store screenshots of Thulla Masters, Thulaa, Bhabhi Thulla Online, Teen Patti Gold, Octro Teen Patti, Callbreak.com, Rung, Court Piece, Mindi (Dehla Pakad), Marriage, Ludo King, Yalla Ludo, UNO!, Spades Plus and Gin Rummy Plus. The UX scan (`competitor-ux-scan.md`) covers behaviour; this note covers the look, down to the small parts. Input to the second UI pass.

## 1. The big picture
- **Landscape wins for trick-taking.** Callbreak, Thulla Masters, Bhabhi Thulla Online, Teen Patti, Spades Plus, Mindi, Marriage and UNO play in landscape. Portrait is the exception (Thulaa, Rung, Ludo). Landscape fits six seats and a 13-plus-card hand without wrapping.
- **The table is a scene, not a screen.** A room or place sits behind the table: a desert, autumn park, stadium or snowfall in Callbreak; a beach, pool, Egyptian hall or study in Thulla Masters; a casino hall with chandeliers in Teen Patti; a city skyline in Mindi. Scenes are the themes players collect.
- **Materials, not flat colours.** Padded leather or carved wood rails, a thin gold inlay line, felt with a texture and the game's logo printed faintly in the middle, glossy plastic buttons, gold-gradient titles.
- **One hero moment per game gets a full-screen effect.** "Spades broken" shatters glass over the table; UNO stamps the screen; Gin Rummy's "Knock" is a big orange badge. For us that is the thulla.

## 2. The table, piece by piece
| Part | What they do |
|---|---|
| Hand | Pinned to the bottom edge and bleeding off it; heavy overlap so only the index shows; very large rank (taller than the suit) with a small suit under it; sorted by suit with colours alternating; sits over the rail, not above the table |
| Own seat | Your avatar sits in the middle of the hand's top edge (Callbreak, Spades Plus) with your score under it; no separate "me" row |
| Opponent seat | Portrait in a thick ring or rounded-square frame; a level number in a small shield on the frame; name on a dark plate below; a flag or status icon; **a small fan of card backs beside the avatar** to show cards in hand (Bhabhi Thulla Online, Thulla Masters); score as a "2/4" pill |
| Seat extras | A gift or throwable button on each seat (Teen Patti, Ludo King); a chat or emote bubble button by the seat (Callbreak); a dealer "D" chip; speech bubbles on the seat |
| Timer | A green-to-red arc drained around the active avatar (Callbreak, Mindi); some put an hourglass in the centre of the trick (Bhabhi Thulla Online) |
| Trick | Each card lands in front of the player who played it, so the trick forms a cross or a fan pointing at the seats; the centre carries the led suit or the timer |
| Felt | Logo watermark, a gold line inset from the rail, a vignette at the edges; perspective (a trapezoid table seen from your chair) in Spades Plus, Teen Patti, Mindi, Marriage; top-down rugs and mats in Thulla Masters and Rung |
| HUD | A round glossy back button top left; settings gear top right; small square icon buttons for the score sheet and round count ("2/5"); currency only outside the table or in one chip |
| Bottom right | A blue SORT or ARRANGE pill and a chat bubble button, both icon-led |
| Cards | Illustrated court cards (Callbreak's cartoon kings, custom J/Q/K art in Thulla Masters); big centre pips; a branded back |
| Event text | Chunky display type with a dark outline and a drop shadow, gold gradients for wins, red for losses, ribbons for "WINNER" and "GOOD LUCK" |

## 3. Around the table
- **Home is a lobby of big illustrated tiles**: 3D tables and chairs for Public, Private and Offline (Mindi), game tiles with art (Teen Patti, Octro), wooden mode cards with a green PLAY button and a player count (Callbreak). A thin top bar holds avatar, level, currency and settings; a bottom bar holds the tabs.
- **Buttons are objects**: glossy gradient, a darker bottom edge, a dark outline, a press-down state. Green for go, yellow or gold for main, blue for secondary, red for danger.
- **Results are a score sheet** on a cream or parchment card with your column highlighted (Callbreak), or a podium with ribbons and crowns (Gin Rummy, UNO).
- **Rung (gg.rung.app)** is the one premium outlier: maroon and gold, engraved small-caps serif, a gold-foil card box as the hero image, a flat top-down table. It shows a classy South Asian look can still read as a game.
- **Thulaa** is the dark, neon, minimal outlier, the nearest to Hulla's current build. Players call it "clean, modern", but it is a small app and its table reads as a list of cards.

## 4. What to avoid
Casino cues (chips, chandeliers, "Boot", dealer models), slot mini-games, spin wheels, stacked currencies and sale badges on the table. These are tied to Teen Patti's money culture and conflict with D2.

## 5. What this means for Hulla
1. Decide orientation first; landscape is the category norm for the table.
2. Build the table as a scene with a themed backdrop, a material rail and a logo watermark on the felt; themes become the cosmetics catalogue.
3. Hand bleeding off the bottom with big indices; my avatar in the hand's top edge.
4. Opponent seats as framed portraits with a level shield, a name plate, a card-back fan and a draining arc.
5. Trick cards land in front of whoever played them.
6. Icon-led glossy HUD buttons; SORT and chat bottom right.
7. The thulla as a full-screen moment with outlined display type.
8. Results as a score sheet or podium with ribbons, never a form.
