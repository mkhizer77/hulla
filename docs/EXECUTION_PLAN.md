# Execution plan

Updated: 11 October 2026. How Hulla gets from an empty repository to a finished product, phase by phase. The tickets live on GitHub (milestones and issues); this file is the map. Decisions the audit of 11 October 2026 raised are listed in `docs/STATUS.md` and gate the tickets named there. The schedule comes from GDD §29.2 and assumes full-time work from 13 October 2026 with no buffer; the GDD allows 20% on top. Dates slip together, never one at a time.

## How to read this
- One GitHub milestone per phase, one issue per unit of work. Issues are listed in execution order and name what they come after.
- Labels say where the work is: `engine`, `protocol`, `server`, `unity`, `web`, `content`, `design`, `ux`, `ops`. `owner` marks a decision or an action only Khizer can take (contracts, accounts, approvals, legal). `epic` marks a later wave that is broken into issues when it starts.
- Every issue points at the GDD sections and ADRs it implements instead of restating them.

## Phases at a glance
| Phase | Weeks | Dates | Goal | Exit |
|---|---|---|---|---|
| M0 Tooling and Thulla engine | 2 | 13 Oct to 27 Oct 2026 | The rulebook runs in TypeScript | The engine passes every vector; traceability 100% |
| M1 Vertical slice | 4 | 28 Oct to 24 Nov 2026 | Server table core and the Unity table, private rooms, Quick Play, voice, quick chat, the thulla moment | A full round with five friends over voice on real devices; 60 fps on the low tier |
| M2 Alpha | 5 | 25 Nov to 29 Dec 2026 | Everything in GDD §1.6 present; full asset sets delivered | Crash-free 99%; load test 1,000 CCU; closed beta running |
| M3 Beta, UI/UX pass, soft launch | 4 | 30 Dec 2026 to 26 Jan 2027 | The polish pass, performance, store and compliance, open testing, soft launch in PK, UK, UAE | GDD §28.5 Beta and Launch gates |
| M4 Launch v1.0 | 2 | 27 Jan to 9 Feb 2027 | iOS pipeline, worldwide release, community | A stable first week |
| M5 v1.1 | 4 | 10 Feb to 9 Mar 2027 | Ranked seasons, phone login, Urdu and Hindi, subscription, tablet landscape | |
| Web client | 10 | 10 Mar to 18 May 2027 | GDD §21 scope | Nightly Unity to web cross-play green |
| Rung module | 10 | 19 May to 27 Jul 2027 | Game two, end to end | Rung passes its own vectors and ships behind a flag |
| M6 v1.2 | 5 | 28 Jul to 31 Aug 2027 | Clubs, gifting, 7 to 8 seats, Bengali and Punjabi | |
| Later waves | | after M6, gated by retention data | Ludo and Tambola, party pack, tournaments, Carrom, optional bots | |

## Unity first
The product is Unity on Android and iOS until v1.0 is live. The order inside that is deliberate: M0 makes the rules executable with no UI at all, so every later screen is built on an engine that already passes the vectors. M1 builds the generic table core on the server and the generic table kit in Unity, then the Thulla module on top of both, so Rung and the catalogue reuse the kit rather than fork it. M2 adds everything else v1.0 ships. M3 polishes and proves it. M4 launches it.

## When the web client starts
After v1.1, so from 10 March 2027, for about ten weeks, before Rung (owner decision, 12 October 2026). The phases after launch run one at a time because there is one developer: M5, the web client, Rung, M6. It starts late on purpose: the mobile app is the product, and the web client is mostly a presentation layer over packages that already exist by then (ADR-0017). Three things land earlier so that stays true:
- the shared TypeScript packages carry no Node built-ins from M0, so the browser can import them unchanged;
- the design tokens file exports CSS variables alongside USS from M1;
- a static link router page at `hulla.gg/j/CODE` goes up in M2 for invites, and the web client replaces it.

## Design and assets
Design is finalised at the start of M1 and assets are produced through M1 and M2, so the full set exists before the polish pass in M3.
- **M1, first two weeks.** Approve the Modern Mehfil style sheet, build the design system (palette, type with script fallbacks, spacing, components and states, motion sheet) and mock up every screen in GDD §14.1 plus the table at three phone sizes. The Unity tokens file is generated from this system, and the 2D contractor brief goes out from it.
- **M1, rest.** The vertical-slice set: one deck, one back, one table, eight avatars, three stamps.
- **M2.** The full sets arrive while features are built: two decks (classic and desi court cards) with large-index and four-colour variants, 12 backs; eight table surfaces, 24 avatars and frames; 16 emotes, the stamp set, the UI icon set; the logotype, app icon, splash, store art and share-card templates; the sound effects set and music; the first voice-over pack is recorded in M3 with season one.
- **M3.** The UI/UX pass integrates everything and rebuilds screens against the mockups.
- **Rung and later games.** Each game has its own design and assets ticket inside its milestone (table layout, phase inputs, stamps, any new surfaces).

Asset inventory, from GDD §15.8 and §16:
| Asset | Count | Lands in |
|---|---|---|
| Card faces | 2 decks of 108, plus large-index and four-colour variants | slice deck in M1, full in M2 |
| Card backs | 12 | M2 |
| Table surfaces | 8 with light setups | 1 in M1, 8 in M2 |
| Avatars and frames | 24 avatars, a frame set | 8 in M1, all in M2 |
| Emotes | 16 | M2 |
| Stamps | THULLA (3 variants), Bhabhi, escape | 3 in M1, set in M2 |
| UI kit and icons | design system components, icon atlas | M1 system, M2 icons |
| Brand and store | logotype, app icon, splash, feature graphics, share-card templates | M2 |
| Audio | SFX set per §16.2, music, VO pack v1 (Urdu, English) | SFX and music M2, VO M3 |

## The UI/UX passes
There are two, because polishing before the features exist wastes the work and polishing after the store screenshots are taken is too late.
- **Pass A, end of M1 (one week).** Table feel only: two playtests of 8 to 12 people on real devices, an argument log of rules disagreements, and fixes for fan reach, legal-card lighting, timer feel, lobby sharing and the thulla moment. It is the vertical-slice exit test.
- **Pass B, first half of M3 (two weeks).** The whole app, once features are complete and the final art exists. Four tickets: final art integration and screen rebuilds against the mockups; motion, transitions and haptics; empty, error and loading states plus a line-by-line microcopy review; the accessibility audit and fixes. FTUE tuning from beta metrics runs alongside, and the performance pass follows.

## Working agreement
Issue by issue, in milestone order. Pick the next open issue, branch as `type/short-description`, open a pull request that closes it, wait for the `ci` check, squash merge. Owner-labelled issues are worked in parallel by Khizer so they never block code. `docs/STATUS.md` is updated at the end of every session; spec gaps go to `docs/OPEN_QUESTIONS.md`.

## Ticket index
### M0 Tooling and Thulla engines (due 2026-10-27)
- #1 Set up the pnpm workspace and root tooling
- #2 Create the .NET solution for the C# engine mirror · deferred to later waves (ADR-0019)
- #3 shared-ts: Result, error catalogue, ids, PRNG and hashing
- #4 Thulla engine: state, rules schema, presets and the deal
- #5 Thulla engine: opening trick, normal tricks, thulla, pick-up and hand-taking
- #6 Thulla engine: two-player endgame and the forced-outcome search
- #7 Thulla engine: redaction, auto-play policy, results and series
- #8 C# mirror of the Thulla engine · deferred to later waves (ADR-0019)
- #9 Shared test vectors and the traceability matrix
- #10 Cross-engine equivalence harness · deferred to later waves (ADR-0019)
- #11 M0 close-out: CI on the real scripts, package READMEs, status
- #130 Trademark search and domain registration for Hulla · owner (in parallel, urgent: the repository is public)
- #131 Latency survey for the first player region · owner (in parallel)

### M1 Vertical slice (due 2026-11-24)
- #12 Protocol schemas: envelope, shell messages and Thulla messages
- #13 Codegen: TypeScript types and validators, C# DTOs, drift check
- #14 Nakama local compose and the server module skeleton
- #15 Table core: lifecycle, message pipeline, event log and snapshots
- #16 Register the Thulla module and run a scripted round server-side
- #17 Table RPCs: create, join by code, presets and voice token
- #18 Quick Play matchmaking with human-only fill
- #19 Reconnection, resume tokens, absence and host migration
- #20 Quick chat and emotes as match data; a chat channel per table
- #21 Create the Unity project with assemblies and the composition root
- #22 Design: finalise the style sheet, the design system and the screen mockups · owner
- #132 Spike: Nastaliq, Devanagari and Bengali with the Advanced Text Generator
- #23 UI Toolkit theme: design tokens, fonts and the base UI kit
- #24 Session: guest device account, token refresh and the Nakama socket
- #25 TableClient, event queue and AnimationScheduler
- #26 Table kit: seat ring, hand fan, pile, timer ring and stamps
- #27 Thulla client module: layout, renderer and input
- #28 Private room: create, lobby, code share and start
- #29 Quick Play: search, widening feedback and auto-start
- #30 Results screen and the rematch vote
- #31 Quick chat strip and emotes at the table
- #32 Voice chat with Agora behind IVoiceService
- #33 The thulla moment: timings, stamp, shake, sounds and reduce-motion
- #34 Assets: vertical-slice set (one deck, one back, one table, avatars, stamps) · owner
- #35 Performance baseline: 60 fps at the table on the low-tier device
- #36 UX pass A: table feel playtests on real devices · owner
- #37 First Android build from a tag · owner

### M2 Alpha (due 2026-12-29)
- #38 Account linking with Apple, Google and Facebook; guest merge
- #39 Account deletion with a 14-day grace period and data export
- #40 Profiles, handles, friend codes and the stats card
- #41 Friends: requests, search, codes, QR, recent players and favourites
- #42 Presence states and the adaptive Home hero
- #43 Invites, share sheet and deep links · owner
- #44 Static link router page at hulla.gg/j/CODE · owner
- #45 Spectate a friend's private room
- #46 Push notifications: invites, friend requests, daily reward, season end
- #47 Text chat with per-language filters, mutes and guest restrictions
- #48 Block and report with automatic context
- #49 Moderation: strikes, bans, admin RPCs and audit log
- #50 XP, levels, titles and per-round coin rewards
- #51 Wallet and append-only ledger
- #52 Missions: three daily, five weekly, one free re-roll
- #53 Daily reward calendar
- #54 Weekly and monthly leaderboards
- #55 Achievements with gem and title rewards
- #56 Content pipeline: schemas, validation, signed manifest and Addressables
- #57 Cosmetic catalog, shop and collection with live preview
- #58 In-app purchases with server receipt validation · owner
- #59 Season pass: 40 tiers, free and premium tracks
- #60 Rewarded and interstitial ads through AppLovin MAX with server-side rewards · owner
- #61 Tutorial: five scripted mini-hands with a coach
- #62 First-time flow, contextual hints and the first-session prompts
- #63 In-table rulebook and preset explainer
- #64 Settings screen and the accessibility baseline
- #65 Localisation-ready pass: string tables, fonts, RTL and pseudo-loc
- #66 Assets: two decks (classic and desi court cards) and 12 card backs · owner
- #67 Assets: eight table surfaces, 24 avatars and frames · owner
- #68 Assets: 16 emotes, the stamp set and the UI icon set · owner
- #69 Assets: app icon, splash, logotype, store art and share-card templates · owner
- #70 Assets: sound effects set and music · owner
- #71 Analytics: typed event schema, consent and batching
- #72 Remote config, feature flags, crash reporting and minimum client version
- #73 Audio and haptics: sound set, voice-over hooks, mix and subtitles
- #74 Share cards from the results screen
- #75 Server observability: metrics, logs, dashboards and alerts
- #76 Phase 0 server: host, compose, Cloudflare, backups and the deploy workflow · owner
- #77 Load test to 1,000 concurrent players with k6 and chaos injection
- #78 Play-mode smoke tests and the device matrix
- #79 Closed beta of 50 friends and family · owner

### M3 Beta, UI/UX pass and soft launch (due 2027-01-26)
- #80 UI/UX pass B: final art integration · owner
- #81 UI/UX pass B: motion, transitions and haptic polish
- #82 UI/UX pass B: empty, error and loading states, and the microcopy review
- #83 UI/UX pass B: accessibility audit and fixes
- #84 FTUE tuning from beta metrics
- #85 Performance pass across the device matrix
- #86 Store listings, screenshots and metadata · owner
- #87 Compliance pack: privacy policy, terms, DPIA, consent and age gate · owner
- #133 UK Online Safety Act assessments · owner
- #88 India legal decision: review or geofence · owner
- #89 Season one content fill and the first voice-over pack · owner
- #90 Backups, restore drill, blue-green deploy and rollback runbook
- #91 Support and moderation playbooks
- #92 Open testing in Pakistan and the UK with KPI tuning · owner
- #93 Soft launch on Android in Pakistan, the UK and the UAE · owner

### M4 Launch v1.0 (due 2027-02-09)
- #94 iOS build and release pipeline · owner
- #95 v1.0 worldwide release on Android and iOS · owner
- #96 Creator seeding and community channels · owner
- #97 First-week live operations: alerts, hotfix path and scale check

### M5 v1.1 Ranked, phone login, Urdu and Hindi (due 2027-03-09)
- #98 Ranked seasons: rating, leagues, placement and rewards
- #99 Phone OTP login · owner
- #100 Urdu and Hindi UI, native-script quick chat, Punjabi and Hindi voice-over · owner
- #101 Hulla Club subscription · owner
- #102 Tablet landscape layout

### Web client (due 2027-05-18)
- #103 Web client scaffold: Vite, React, PixiJS and the shared packages
- #104 Link router: open the app or load the web table
- #105 Guest session and the table client with nakama-js
- #106 Table rendering in PixiJS: seats, fan, pile, timer and the thulla moment
- #107 Shell screens: Home, private room, Quick Play, results, profile, friends
- #108 Text, quick chat and emotes on the web
- #109 Voice on the web with Agora's web SDK
- #110 Cosmetics display and equip; pass purchase through Stripe · owner
- #111 PWA, CDN hosting, preview deployments and Lighthouse budgets
- #112 Conformance suite and nightly Unity to web cross-play

### Rung module (due 2027-07-27)
- #113 Rung rules specification with rule IDs · owner
- #114 Rung shared test vectors and traceability
- #115 Rung TypeScript engine
- #116 Rung C# mirror and equivalence
- #117 Register match_rung, presets and 2v2 matchmaking
- #118 Rung design and assets: table layout, trump and court visuals, team colours · owner
- #119 Rung client module: layout, renderer, trump call input, rulebook, results
- #120 Rung tutorial, quick-chat contexts, missions and catalog tags

### M6 v1.2 Clubs, gifting, big tables, Bengali and Punjabi (due 2027-08-31)
- #121 Clubs (Adda): groups, chat, standing room and game nights
- #122 Gifting cosmetics through IAP
- #123 7 and 8 seat tables with two decks
- #124 Bengali, Punjabi and Arabic UI and the Bengali quick-chat pack · owner

### Later waves
- #125 Epic: Ludo and Tambola
- #126 Epic: Party pack (Antakshari, Raja Mantri, Name Place Animal Thing, Hand Cricket)
- #127 Epic: Tournaments (v1.3)
- #128 Epic: Carrom (real-time physics track)
- #129 Epic: Bots, Play vs AI, offline rounds and Daily Challenge (optional, D7)
