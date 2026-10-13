# Competitor UX scan (October 2026)

What the apps our players already use do well and badly at the table, in the lobby and around them. Input to the UX/UI pass (`docs/design/ux-ui-pass.md`). Store pages, recent reviews, release notes and public teardowns were read; nothing here comes from playing the current builds, so the hands-on details marked *(h)* need a check against recent gameplay captures before they become specs.

## 1. Direct competitors: Bhabhi and Thulla apps

| App | Size and rating | What it does | What players say |
|---|---|---|---|
| Thulla Masters: Bhabhi (Sirra) | Play 3.2★ from 2.15k, 100k+; iOS 4.6★. Launched June 2025, the most active | 3, 4 or 5 seats, every table a coin buy-in; thulla animations, throwables, emotes and avatars sold as cosmetics; trophies, leagues, weekly boards, two spin wheels, an ad-rewards track | A July 2026 update put an ad after every private match and bounced everyone to the menu; the longest reviews mourn "press Ready and continue playing together". Rewarded ads that pay nothing, chat behind ads, a disconnect handing the seat to a bot, buy-ins that feel unfair. Asked for: six seats, a longer timer |
| Bhabhi Thulla Online (NewLeaf) | Play 3.9★, 50k+; iOS 4.6★ | 3 to 6 seats, Casual to Legends lobbies, clubs, buddies, world chat, titles | Hidden bots ("bots ruined the whole game"), suspected desync, coins lost on a phone change |
| Thulaa (Keeda) | Play 4.5★, 10k+; iOS Dec 2025 | Four seats only, match "in about a minute", one room code for the group, voice at the table, emotes, anonymous sign-in, EU and South Asia servers, "no chips" | "Finally a proper Bhabho game, clean, modern"; the tutorial is praised; the canned voice lines are not |
| Octro Bhabho | Play 2.9★, 100k+ | Legacy | Abuse in open chat, "not clear how to invite friends", no vibration or your-turn sound settings, leavers replaced by bots |
| OEngines Bhabhi | Play 4.4★ | Boot tables of 3 to 6 | "Connection lost" before a win, an ad on every action, an instant results screen that makes opponents look like bots |
| Bhabhi Offline (Unreal) | Play 4.6★ from 25k, 1M+ | Three handicap levels, a remaining-cards tab, trick history | Ads with no close button, fake "online" players |
| New in 2025 to 2026 | All small | Voice (AH Studios), six-character codes that auto-fill from the invite (AuraGames), code, QR or link plus "pile-pick animations" (Shglobalmart), "bots are clearly marked" (logicstack), pass-and-play with a clear timer (AllianceTech) | The newest entrants all sell themselves against casino chips, fake players and ads. That pitch is now expected, not a differentiator |

Rung: OEngines' Court Piece (4.2★) has banner ads covering the hand and a frozen rules screen; CourtPiece Multiplayer sells chat and emoji packs at $0.99 to $4.99 and reviewers say most opponents are AI; Vonterra's new Rung (gg.rung.app) has no ads, peer voice, a four-letter code and house-rule toggles.

**Lessons, ranked**
1. Keep the group at the table between rounds: one Ready, the next deal, no menu bounce, no interstitial in private rooms.
2. Disconnects are visible and recoverable: a grace window before auto-play, a reclaimable seat, nothing silently lost.
3. Label bots and auto-played seats, on the table and in results.
4. Invites are code, link and QR; the invite fills the code; add friends from the results.
5. Stage the thulla: a distinct sound, the pile flying to the picker, the count climbing, a compressed hand after big pick-ups.
6. Reward every finishing place, not only first.
7. Information aids at the table: trick history, played suits, the rulebook mid-game.
8. Chat safe by default: quick chat, emotes, friends-only voice.
9. Basic settings: vibration, a your-turn sound, volume, turn length in private rooms, six seats.
10. Purchases honoured everywhere, progress survives a phone change.

**Never:** ads over cards or without a close; Remove Ads that still shows ads; unlabelled bots or "random card" auto-plays; chat or the next match behind an ad; buy-ins, spins and chip bundles.

## 2. Category leaders

| Product | Worth copying | Worth avoiding |
|---|---|---|
| Ludo King | Corner pods with a timer ring per seat *(h)*; voice in friend rooms; code shared by WhatsApp first; pass-and-play | "Keeps spamming cheap ads"; server kicks; the rigged-dice belief, which one designer answers with roll-probability transparency |
| Callbreak (Teslatech, Bhoos) | One interstitial after a whole game, never between hands; labelled bots fill a short friends table; bid against tricks "2/4" on each seat *(h)* | Real-money entry breeds bot distrust |
| UNO! Mobile | Playable cards rise, others dim; tap or drag to play; a shout button that appears at two cards; full-screen stamps and a shake for wild cards; 2v2 and room mode with house rules | Four currencies and stacked events clutter every screen; "pay real $$$ to play" |
| Carrom Pool | Polish, haptics, a clean progression | Voice behind the pass; long ads; "menu graphics blend together" |
| Spades Plus | A per-table chat toggle | Emotes and the card tracker behind a subscription read as pay-to-win |
| Hearts and Solitaire leaders | Auto-play of forced moves, large cards, four-colour deck, left-handed layout, calendar streaks | |
| Teen Patti Gold | Throwable joke items that land on a seat; a speaking ring per avatar | The vocabulary |
| Words With Friends | Home grouped into "Your move" and "Their move" | Nudges nobody answers |
| Jackbox, Kahoot | Four-letter codes without I, O, 0, 1; code, name, play; the lobby fills live | |
| Stumble Guys, Play Together | A party bar of friends' avatars at the top; invite toasts over any screen; follow the leader into a match *(h)* | |
| Balatro | Hover lift 12 px and 1.05 in 150 ms, select 24 px and 1.08 with a glow, ±3° tilt, shake tiered 2, 4, 8 px, each scored card a step up a scale, score roll-up with overshoot | |

Cross-cutting complaints, in order: ads that interrupt play; suspected rigged RNG; bots passed off as humans; disconnects; features behind a pass; too many currencies and events.

**Recommendations, ranked**
1. Fairness you can see: a "Fair deal" chip on the results that opens the commitment and reveal (ADR-0009); an "Auto" badge on any auto-played seat.
2. Legal cards rise and keep their colour; the rest dim with the reason on tap; highlight only on your turn.
3. A Balatro-weight hand: lift on touch, tilt toward the finger, select lifts further; flick or tap again plays; reduce motion respected.
4. Tiered juice: a plain trick gets a thump and a 2 px nudge; a thulla gets the stamp, a 4 px shake, a haptic and the flight; the last pick-up or escape gets a short slow-motion beat. Everything legible within 400 ms.
5. A rising pentatonic note for each card added to the trick, reset at the trick end.
6. Throwables aimed at a seat, long-press to send, rate-limited, mutes respected, a free starter set.
7. A one-tap quick-chat strip above the hand, contextual; long-press opens the full set; messages appear as bubbles on the sender's seat.
8. Voice free and one tap away: a mic on your seat, a speaking ring on every avatar, long-press a seat to mute.
9. The active seat's ring drains and shifts amber then red over the last 5 s, with a tick and one haptic at 3 s.
10. Four-character codes, letters only, no I, O, 0, 1; WhatsApp first; the lobby fills live; no account to join.
11. Home ordered "Your move", then "Live now", then "Start"; a party bar at the top; one progression banner, the rest as badges.
12. Reconnect as a slim pill with the seat-hold countdown, never a modal; others see a grey avatar with a wifi glyph, then "Auto".
13. Rematch in one tap with avatars filling as people accept and a 10 s countdown; the host edits rules from the same screen.
14. Ads only between rounds, never between tricks, none in private rooms in the first sessions; rewarded ads opt-in with a visible cap.
15. One currency on screen; pass and missions as badges; rewards roll up for 2 s and get out of the way.

## 3. Poker, Teen Patti and party games

Studied for table and social mechanics only; none of the vocabulary or economy crosses over.

| Product | Worth copying | Worth avoiding |
|---|---|---|
| EasyPoker (friends only, no chips) | Four-digit PIN, no account to play; voice that keeps running with the phone locked; host kick; turn and new-hand pushes | Time-outs with no network error shown |
| Pokerrrr 2, PPPoker | Create a game, get a code, done; clubs only for several tables at once; scheduled and auto-renewing games; disconnection protection gives extra action time; a settings toggle that hides throwable animations | Freezes on app switch; distrust of outcomes |
| Zynga Poker, WSOP, Poker Heat | Opponent plate is avatar, name, last action and count; timer, hand and actions in one zone at the bottom; player photos for a real-table feel | "A dozen pop-ups each time the app opens"; dismiss buttons that line up with buy buttons; ads that cost a turn; invites that do not seat the friend at your table |
| Teen Patti Gold, Octro, Teen Patti Live | Large cards, minimal but effective animation, adjustable text and contrast; invite-only tables; small joke gifts that tease a seat; "zero wait" to a table; private chat with block | Everything else |
| Yalla Ludo (Gulf and South Asia) | The voice room is the product and the game sits inside it: up to 15 mic seats, the room shrinks to a widget while a round runs; a speaking ring on the seat; stickers mid-game; nameplates and entry effects as cosmetics; friends list with Spectate; fun stats on the results | Paid advantage ("the game favors those who buy"), an unpredictable auto mode, no screen-reader support |
| Plato (chat first, 50 games, no ads) | Game moves arrive as chat messages; invite is a room link that lands the friend in the room; "never pay-to-win"; no ads is the top praise | Leavers with no resign; "blocking does absolutely nothing" inside rooms; a word filter that blocks "Japan" |
| Hago, Bunch | Play, get paired, add as friend; friends' faces float over the game; launching together drops everyone into one session | |
| Jackbox, Kahoot | You know you joined when your name appears on the screen; the first joiner owns Start; overflow becomes the audience; link or QR skips typing; the PIN expires with the game | |
| Among Us | A quick-chat wheel of seven categories as the safe default; under-13s get quick chat only; shape and text coding after colour-only was called out | |
| Psych!, Heads Up! | The reveal screen is the celebration; players are the content | |

**Recommendations, ranked**
1. Four-character codes plus a link and a QR code; guests never type more than four characters.
2. The lobby is the gathering: your seat lights up the moment you connect; the host alone owns Start; late joiners watch.
3. The voice room survives the table: the friends' room is the home of the group, the table a widget inside it, the room a pill while a round runs (with Agora, #33).
4. A speaking ring on the seat, free, readable at six seats.
5. One zone for the turn: timer, hand and actions in the bottom third; opponent plates show avatar, name, last action and count.
6. Throwable teasers between seats with a per-player mute and a "hide animations" toggle; cosmetic only.
7. Quick chat as the first-class input; free text opt-in.
8. A social results screen: add-friend chips, fun stats, rematch countdown, "same room, next game".
9. Scheduled and recurring tables: the host saves the Friday game, a push ten minutes before (later).
10. A join-a-friend hero that seats you, with the refusal reason when it cannot (full, private, in progress).
11. Reconnect grace with extra action time and "reconnecting" on the seat for everyone.
12. One hero, one row of rooms, nothing modal on launch; live-ops cards below the fold.
13. A player sheet on tap with avatar, handle, level, mutual friends, add, mute, block, report; a block hides the player in rooms too.
14. Room identity cosmetics: nameplates and entry effects alongside the felt (catalog, later).
15. Screen-reader labels on seats and cards; shape and text on suits.

**Never:** pop-ups stacked over the table; league and album chrome during a round; invites that fail silently; paid advantage of any kind, including auto modes; a filter that blocks place names or a block that stops at the door.

## 4. What this means for Hulla, in one list
Fairness you can see; stay together between rounds; one thing at a time on screen; the thulla as the hero moment with tiered juice; quick chat and throwables as the social layer, voice free; codes of four characters with link and QR; reconnect as a pill with a seat hold; labelled auto-play; a results screen built for the group; information aids (voids, picked-up tray, trick history) that mirror what an attentive player remembers.

## Where this goes
The design in `docs/design/ux-ui-pass.md` turns this into screen specs. GDD §3 (private) keeps the market table; this file holds the UX reading.
