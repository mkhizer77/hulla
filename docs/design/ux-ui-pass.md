# UX and UI pass: design

The screens and game elements Hulla ships with, specified closely enough to build. It follows GDD §14 (UX and UI), §15 (art direction), §16 (audio and haptics) and §17 (onboarding), and the reading of the market in `docs/research/competitor-ux-scan.md`. Where this file and the GDD differ, this file is the newer decision and the GDD is updated to match.

Final art (decks, avatars, tables, the UI kit) is the owner's track (#34, #80). Everything here is designed to look right with the placeholder faces and avatars we have today and to slot the art in without changing structure.

## 1. What the pass is for

The vertical slice has every feature and no feel. The research says players in this category decide in the first two rounds on four things: does the table feel like the real one, is it fair and honest, can I get my friends in without thinking, and does anything interrupt the trick. The pass fixes those four.

Decision rules (GDD §4.1) still settle arguments: never interrupt a trick; public at the table is public in the app; one hand, one thumb; the host's rules are law; juice the social beat, not the card; low-end Android first; no gambling vocabulary.

Three more, learned from the competition:
- **Fairness you can see.** The fair-deal commitment, auto-played seats and bots are labelled, always.
- **Stay together.** Nothing between rounds sends the group back to a menu. Rematch, rules edits and invites happen at the table.
- **One thing at a time on screen.** One currency, one progression banner, one event surface. Pass and missions are badges until opened.

## 2. Shell and navigation

**Bottom navigation** (GDD §14.1): Home, Friends, Shop, Pass, Profile. Shown on those five screens and the screens they open (Missions, Leaderboard, Achievements, Settings); hidden in Quick Play, the lobby, the table and the tutorial. 48 dp targets, label under icon, the active tab in saffron. Badges: Friends shows pending requests and friends at a joinable table; Pass shows claimable tiers; Shop shows the weekly spotlight once per week.

**Transitions** (GDD §15.7): a 240 ms cross-fade with a 24 px rise on push, the reverse on back; reduce motion makes it a 120 ms fade. The table and results never animate in with a shell transition; they appear under their own choreography.

**Pills, not modals.** Reconnecting, voice state, Away and "rules changed" are pills at the top centre of the screen: rounded, small text, no buttons. Reconnecting counts down the seat hold ("Reconnecting · seat held 0:45"). Dialogs are for one question with two answers and nothing else.

**Toasts over any screen** for table invites and friend requests, with one action ("Join"), dismissable by swipe, 6 s.

**Empty, error and loading states** on every list: a loading row of three pulsing bars, an empty state with one sentence and one action, an inline error with Retry. Never a blank screen.

## 3. Theme

Tokens in `Hulla.uss`, every colour and size from these; no literals in screens.

| Token | Value | Use |
|---|---|---|
| `--surface-ui` | #141518 | Screens |
| `--surface-panel` | #1C1E22 | Cards, panels |
| `--surface-raised` | #2A2D33 | Buttons, chips, inputs |
| `--surface-table` | #1F2A2E | Table default, behind the felt |
| `--felt`, `--felt-deep`, `--rail` | #1E6B48, #0E2219, #6B4423 | The default table surface until the tables land |
| `--accent-saffron` | #F4A300 | Primary actions, active tab, legal cards, the leader crown |
| `--accent-teal` | #16A085 | Secondary actions, safe states, Ready |
| `--accent-magenta` | #D81B60 | Pass and premium |
| `--accent-mustard` | #E0B000 | Coins, the timer's amber phase |
| `--ink-ivory`, `--ink-muted` | #F6F1E7, #9AA0A6 | Text |
| `--state-thulla` | #E53935 | Stamp, danger, the timer's last 2 s |
| `--state-escape` | #43A047 | Escape, success |
| `--suit-red`, `--suit-black`, `--suit-blue`, `--suit-green` | #D32F2F, #1B1B1F, #1E6FD9, #2E7D32 | Pips; blue and green for the four-colour deck |

Type scale at the 1080 px reference: title 96, heading 56, body 34, small 26, caption 22, stamp 110, code 120 with 12 px tracking. Tabular numerals for counts and timers once the UI face lands (§15.3); until then the default face in bold.

Spacing 8, 16, 24, 32. Radius 14 on panels and buttons, 999 on pills and chips. Tap target 132 px (48 dp).

Buttons: primary (saffron on ink), secondary (teal outline, teal text), quiet (muted text, no fill), danger (thulla red outline). Chips: default raised, on (saffron), emote (dark gold). All with pressed scale 0.97 over 80 ms.

High contrast swaps the surfaces to black and the muted ink to #E8E8E8 and keeps the accents; the contrast test (`ThemeContrastTests`) covers both themes and the new tokens.

## 4. Home

Order from the top, in a scroll:

1. **Header row:** avatar with level ring, name, coins. Nothing else. The dev server field keeps its place under the header in development builds.
2. **Party bar:** friends online as avatar circles with a presence dot; tap to see their state or join their table. Empty: "Invite your group" with Share.
3. **Hero:** adaptive (GDD §14.10). Friends at a joinable table: "Join Ali's table · 2 seats open" in saffron, with a watch variant when no seat is free. A table you left mid-round: "Back to table 7KQ2M". Otherwise **Play now** (Quick Play with the last seat count).
4. **Create a room** (secondary) and **Join by code** (a four-field code entry that opens from a quiet button).
5. **Daily reward card** when claimable; a pass progress strip with the tier count; the shop spotlight as one small card with the week's countdown. Missions and leaderboards are reached from the Pass and Profile tabs and show as badges here, not cards.
6. **Offline:** the hero becomes "How to play", the party bar shows last-seen states, play buttons are dimmed with "No connection. We'll reconnect you the moment it's back." underneath.

Name entry leaves Home: the first launch asks once (GDD §17.2, step 2) and Profile edits it.

## 5. Quick Play

A centred searching state: the seat-count chips (4, 5, 6) stay editable while searching, a pulsing ring with elapsed seconds, "Widening the search…" after 20 s, Cancel. On error: the message and Retry. Found: a 1 s "Table found" beat with the four avatars, then the table.

## 6. Lobby

Keep the group together while it fills (Kahoot, Jackbox).

- **Code** in 120 px type with Copy and Share; Share opens the system sheet with WhatsApp first. The preset summary in one sentence under it for guests; the host sees Edit rules.
- **Seat ring:** the same `SeatArc` as the table, so the lobby becomes the table without a re-layout. Empty seats show a dotted circle with "Invite"; the host's seat has a small crown; ready seats get a teal tick; each seat that joins drops in with a 220 ms bounce and a soft tap sound.
- **Voice toggle** on your own seat once voice lands (#33); until then the slot is hidden.
- **Ready** (teal) for guests, **Start** (saffron) for the host, enabled at three seats; public tables show the auto-start countdown on the Start button itself.
- Lobby chat stays as a strip of the last three lines above the bar; tap to open.
- Joining is confirmed by your own seat lighting up with a bounce; the invite link lands the friend in the lobby, never on Home. A refused join says why in one line (full, in progress, private).

## 7. The table

### 7.1 Layout
GDD §14.3 stands. Changes:
- **Top bar:** Leave on the left, the room code and the discard count centred with the fair-deal tick, Rules and a **Score** button on the right. Score opens the round scoreboard (room points, Bhabhi tally, tricks this round) as a sheet.
- **Opponent badges:** avatar 116 px with a draining timer ring on the active seat, card-count chip, name, a state pill (Away · auto, Out), the **crown** on the leader, a **thulla flag** for 1.2 s on the seat that gave a thulla, void chips (§7.5), a speaking ring for voice.
- **Felt** with the pile centred; cards land askew by seat.
- **Quick-chat strip** above the hand: three contextual chips plus "…" opening the full panel. Hidden while a card is raised.
- **Hand**, fanned, legal cards lit.
- **Comms row:** mic (hidden until voice lands), emotes, chat. Left-handed mirrors it.

### 7.2 Hand and card interaction (GDD §14.4)
- Touch down lifts the card 12 px and scales it 1.05 in 120 ms; drag along the fan magnifies the card under the thumb; release raises it (24 px, 1.08). Tap again or flick up plays. One-tap play as a setting.
- Legal cards rise 20 px with a saffron edge; illegal cards dim to 40% and show the reason on tap ("Must follow ♥"), the reason pill opens the rulebook's "why not" section.
- A single legal card raises itself and plays after 700 ms unless the player touches the hand (R44, GDD §14.4). The tutorial turns this off.
- Cards that arrive after a pick-up are highlighted in the fan for 1 s after the re-sort.

### 7.3 Turn and timer (GDD §14.5)
- Turn start: hand brightens, a light haptic, the ring begins on your badge and on the active opponent's badge.
- Ring colour: saffron, then **mustard with a pulse at 5 s**, then thulla red at 2 s with a faster tick. A soft tick at 5 s and 2 s.
- The numeric countdown shows only in the last 10 s.

### 7.4 The thulla moment (GDD §14.6), tiered
| Tier | When | What |
|---|---|---|
| Plain trick | The leader takes a trick with no thulla | Card thump on each play (pitch by rank), a 2 px nudge on resolve, the pile fades to the discard |
| Thulla | A card off-suit ends the trick | 0 ms slam with a 2 px, 80 ms shake and a slap; 120 ms stamp (one of three angles) with a stamp sound and a medium haptic for the giver, heavy for the picker; 450 ms the pile flies to the picker's badge, 40 ms stagger, the count bumps on each arrival; 900 ms a two-note sting for the picker; 1100 ms the picker's hand re-sorts with the new cards lit and the crown moves. Total hold 1.2 s |
| Big pick-up | Five or more cards | The stamp text grows to the "Oye hoye!" variant and the quick-chat strip refreshes to the big-pick-up phrases for everyone |
| Escape | A player goes out | A short chime, a success haptic, the badge greys with "Out" and a small confetti burst over it (none under reduce motion) |
| Bhabhi | The round ends | The results slide in after 1 s; the Bhabhi seat gets the stamp and the failure haptic |

Reduce motion swaps flights for fades and removes the shake and confetti; the holds stay so the beat still reads.

### 7.5 Card-memory assist (GDD §14.7)
Under each opponent, grey suit glyphs for suits they are certainly void in: a seat that plays off-suit is void in the led suit; picking up a trick clears the voids for the suits picked up; the round start clears all. A tray icon opens the cards they have picked up this round. Public information only. On when the table's `cardMemoryAssist` setting is on; off in ranked.

### 7.6 Quick chat, emotes, throwables (GDD §11)
- The strip shows three phrases for the current context (`gave_thulla`, `received_thulla`, `opponent_slow`, `leader_self`, `any`) from the quick-chat pack, refreshed on each event; "…" opens the panel with the tabs.
- Messages appear as a bubble on the sender's badge for 3 s; mine above my hand.
- Throwable emotes: long-press a badge opens a ring of the throwables; the chosen one arcs to the badge in 400 ms and sits for 1.5 s. Rate-limited to one per 5 s per sender; mutes hide them; a "hide thrown emotes" setting turns the animations off for the viewer.

### 7.7 Reconnection and absence
Mine: a top pill with the seat-hold countdown; the hand dims; no modal. Theirs: the avatar greys with a wifi glyph and "Away · auto" once auto-play takes over. Rejoin lands in the current trick with a 1 s catch-up.

### 7.8 Haptics (GDD §16.4)
Turn start light; card play soft; thulla given medium; thulla received heavy; escape success; Bhabhi failure. Android uses amplitude where the device allows it; iOS uses the system patterns through a small native shim later, `Handheld.Vibrate` only for heavy events until then. All behind the Haptics setting.

### 7.9 Sound (GDD §16.2)
A placeholder set synthesised at start-up (no assets yet): deal riffle, card tap with pitch by rank, the trick's rising pentatonic step per card, slam, stamp, pick-up slide scaled by pile size, escape chime, two-note sting, tabla-like tick, UI tap. Separate SFX and music sliders; music stays off until the score lands. The recorded set (#16 audio track) replaces the clips, not the calls.

## 8. Results (GDD §14.8)
Podium of avatars in escape order with "1st" to "nth", the Bhabhi seat with the stamp and the table's title, room points and the Bhabhi tally, two highlights ("Biggest pick-up: Noor, 9 cards"), the reward roll-up over 2 s with the optional 2× ad button (when ads exist), a **Fair deal** chip that opens the seed commitment and reveal in plain words, "Add friend" chips for new acquaintances, Share, and **Play again** with the accepted avatars filling along the button and a 20 s countdown. The host gets "Change rules" here. Nobody leaves the table unless they tap Leave.

## 9. Friends
Party-first: online friends at the top with Join or Watch, then requests, favourites, all friends, recent players, blocked. Add by code at the top. Empty state: "Nobody here yet. Share your code." with Share. A tapped friend opens the player sheet (avatar, handle, stats line, Add or Favourite, Invite, Mute, Block, Report), the same sheet as the table's.

## 10. Shop, Pass, Missions, Leaderboards, Achievements
- Shop: category chips, the spotlight card with the week countdown, a grid of items with rarity edge and price in coins, Equip on owned items, Buy with one confirm. Empty category: "Nothing here this week".
- Pass: the tier track as a horizontal scroll with the current tier centred, free row above premium row, claimable tiers glowing, premium in magenta; progress bar to the next tier. Between seasons: the end date and the next start.
- Missions: cards with a progress bar, the reward, Claim when done; the free re-roll chip. Empty: "New missions at midnight".
- Leaderboards: as built, plus a loading row and Retry.
- Achievements: icon placeholder, progress bar, unlock date. Empty: "Play a round to start".

## 11. Profile and Settings
Profile: avatar with frame, name, handle, friend code with Copy and Share, the stats card, titles, achievements, Edit. Settings grouped: Play (one-tap, sort, rules recap), Look (four-colour deck, contrast, large cards, text size, reduce motion, left-handed), Sound and feel (SFX, music, haptics), Notifications, Privacy, Account (export, delete), Help.

## 12. Onboarding (GDD §17)
Keep the flow. Changes: the name step is its own screen with a generated name and Change; the "Do you know Bhabhi?" gate follows; the tutorial's coach bubble sits above the hand and never covers the pile. The first five rounds show one hint each (GDD §17.4), including the void chips and voice once they exist.

## 13. What ships in this pass and what waits

**Now (code, no art):** the shell (nav, transitions, pills, toasts, states), the theme tokens and button variants, Home, lobby ring, the table changes in §7.1 to 7.9 with synthesised sound, results, the empty and error states on every list, the Friends and Settings regrouping, the design tests (contrast, layout, timeline budgets).

**The placeholder look (shipped 13 Oct):** until the art lands the client carries two OFL typefaces (Lilita One for display, Nunito for UI, in `Presentation/Theme/Fonts`), textures generated by `tools/artkit/generate.py` (the felt, the wooden rail, the dark ground with its faint lattice, the card back, gradients, a nine-slice panel with its shadow, glows, the rubber-stamp frame), card faces drawn as vectors (`CardFace`), shaded avatars and depth on buttons and chips. `ScreenSnapshots` renders Home, the lobby, the table and the results to PNGs for a look before a build. The art replaces these files; the structure stays.

**With art (#34, #80):** decks, avatars and frames, table surfaces, the icon set, the display and UI faces, emote art, the stamp set.

**With their features:** voice controls and the friends' voice room that outlives the table, shown as a pill during a round (#33), throwable rate limits on the server, the 2× ad button (#60 client), the Fair deal reveal view (needs the reveal RPC), the scoreboard's series view.

Playtests (#36) run on the result of this pass: two groups of 8 to 12 on real devices, an argument log, and fixes before soft launch.

**Later, logged for the roadmap:** scheduled and recurring tables with a reminder push; nameplates and entry effects in the cosmetic catalog; QR codes on the lobby; screen-reader labels on seats and cards.

## 14. Open questions
Logged in `docs/OPEN_QUESTIONS.md`: the single-legal-card auto-play delay (700 ms interim), the throwable rate limit (one per 5 s interim), whether the quick-chat strip's three slots are the player's picks or the context's (context's, interim).
