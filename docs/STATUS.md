# Status

Updated: 13 October 2026 (morning)

## Phase
M0 complete: the Thulla rulebook runs in TypeScript with full traceability. M1, the vertical slice, starts with the protocol schemas.

## Done
- Fixes merged (13 Oct, PR 199): leaving the guest name keeps the free rename; a returning seat gets a full turn timer.
- M2 #52 server part merged (13 Oct, PR 195): mission pools in content, seeded daily and weekly draws, progress from round events, idempotent claims and the free re-roll. OQ-022 logged.
- M2 #50 and #51 server parts merged (13 Oct, PR 193): XP, levels, titles and coins per round from the GDD tables, the login streak and the daily soft cap at local midnight, level grants, and the append-only ledger with idempotent source ids and no transfers. OQ-021 logged.
- M2 #49 server part merged (13 Oct, PR 190): strikes and bans with expiry, device bans, the ban check at join, audited admin RPCs and round replay from the log. OQ-020 logged.
- M2 #48 server part merged (13 Oct, PR 186) and the preset settings fix (PR 187): blocks both ways on the friend graph, the matchmaker exclusion, reports with the table's chat and the auto-mute; a preset's table settings now apply from a preset-only selection. OQ-019 logged.
- M2 #47 server part merged (13 Oct, PR 184): the profanity filter in six scripts with evasion-proof matching, Standard masks and Family blocks, hot-reloadable lists, mutes per account, the guest text lock at public tables. OQ-018 logged.
- M2 #45 server part merged (13 Oct, PR 181): spectators are friends of a seated player, the host seats them, late arrivals take free seats at the round end; `seat_spectator` and `table_state.spectators` in the protocol. OQ-017 logged.
- M2 #42 server part merged (13 Oct, PR 172): presence states from Nakama's online flag, the seat pointer, the Away flag and the live match label; `table_list_friends` ordered for the Home hero; an in-memory Nakama now drives every RPC, hook and match callback in unit tests.
- M2 #41 server part merged (13 Oct, PR 169): add by friend code with the 500 cap, ten pinned favourites, Recent Players written at round end; requests and blocks ride Nakama's friend API. OQ-016 logged.
- M2 #38 server part merged (13 Oct, PR 168): guest merge by one-time code, one active table session per account with resume-token takeover; provider linking is the client's call once the owner configures the providers. OQ-015 logged.
- M1 #17 to #20, M2 #39 and #40 merged (13 Oct): room codes with region and expiry, `table_join_by_code`, `presets_get`, `voice_token` with a pure-TypeScript Agora token builder (OQ-011); the matchmaker hook with public tables that auto-start, `seat.party` and `table_state.autoStart`; the scripted reconnection suite; quick chat, emotes and table text with the before-message hook (OQ-012); profiles, handles, friend codes and the stats card (OQ-013); account deletion with a 14-day grace period, export and the scheduled purge, with a Nakama paging quirk guarded everywhere (OQ-014). Unity #29 Quick Play merged (PR 167).
- M1 #16 merged (13 Oct): the Thulla host, named match callbacks, the game registry, `table_create`, and the scripted four-seat round against Nakama in Docker. Nakama facts learned and recorded (ADR-0022, ARCHITECTURE §8.1, server README): handlers resolve by global name, the globals freeze after InitModule, the match state is rebuilt as plain data between callbacks, and a message target needs Nakama's node. Server-side validation moved from zod to compiled checks. A native Nakama plus Postgres runs on the dev Mac for server tickets.
- Unity, 13 Oct overnight (PRs 170 to 191): the table kit in UI Toolkit (seat arcs for 3 to 6, fanned hand with tap-to-raise and flick, timer ring, fly-in pile, illegal-tap reasons; #26 open for sprites), settings and the accessibility baseline (four-colour deck, contrast, 200% text, large cards, left-handed, 48 dp, a contrast test; part of #64), the in-table rulebook (#63), table and lobby text chat with mutes and plain-word refusals (#47), profile and friends screens (#40, #41), presence on Home with the join-a-friend hero and Away (#42), room-code sharing (part of #43), fuller results with add-friend chips and the rematch countdown, the host rules editor, the five-step tutorial with the first-launch gate (#61) and first-rounds hints (part of #62), export and delete in settings (#39), the player sheet with block and report (#48), watching a friend's table and seating watchers (#45). Fixes: a card drawn from your hand now leaves it (R38), no turn shown after a thulla (R20), the typed name reaches the profile, rejoins send the resume token. Play-mode tests cover rounds, reconnect, Quick Play, chat, friends, presence, spectating, reports and account rights against the compose server.
- Unity first playable (13 Oct): #21 project and assemblies, #24 session and socket, #25 TableClient and scheduler, #27 Thulla table, #28 private room, #30 results and rematch, #31 quick chat and emotes, the client side of #19 (reconnect and rejoin), local development builds and `docs/runbooks/play-test-locally.md` (PRs 152, 153, 159, 162 to 164). A play-mode test runs three real clients through a full round, chat and a mid-round reconnect against Nakama. The table kit is UI Toolkit with text card faces until the slice art (#34). The Unity CI job waits for the `UNITY_LICENSE` secret; tests run locally until then.
- M1 #15 merged (12 Oct): the table core in `server/nakama/src/match` (lifecycle, pipeline with idempotency, deadlines and auto-play, log with state hashes, snapshots, redaction dispatch, rematch and series) and its Nakama binding; OQ-009 and OQ-010 logged.
- M1 #14 merged (12 Oct): `server/nakama` with the local compose, Dockerfile, config, InitModule with a hello RPC, the esbuild bundler, a fake nk for unit tests and a CI compose smoke test.
- GDD v1.3 (private, git-ignored at `docs/GDD_Hulla.md`): rules spec with IDs, platform catalogue, economy, UX, compliance, production plan.
- Architecture, tech stack, project structure, coding rules, contributing guide, 18 ADRs, research notes.
- Repository skeleton, CI workflow files, issue and PR templates, licence, M0 brief, GitHub and CI setup runbook.
- Public repository `mkhizer77/hulla` on GitHub. `main` protected: pull request required (no approvals, so the owner can merge), `ci` check required, linear history, squash merges only. Workflow token read-only. `phase0` environment with the owner as required reviewer.
- CI on GitHub: `detect` decides which parts exist, `ci` is the single required check, `release-android` calls `ci`, `nightly` runs the 10,000-round equivalence harness, secret scanning, Dependabot and CODEOWNERS in place, LFS cached, deploys pinned by commit SHA.
- Execution plan (`docs/EXECUTION_PLAN.md`): ten phases on GitHub milestones, 133 issues in execution order, design and asset track, web client after v1.0, two UI/UX passes.
- Audit of design, architecture and tech decisions (11 Oct): rule gaps logged as OQ-001 to OQ-006 with interim choices, document drift fixed, owner tickets #130 to #133 added, decisions below.
- M1 #12 merged (12 Oct): `@hulla/protocol` draft-07 schemas, opcode registry with redaction classes, Appendix D fixtures.
- M0 closed (12 Oct). #9 merged: `@hulla/test-vectors` with the draft-07 vector schema, 300 vectors (64 authored, 236 exhaustive small endgames), the TypeScript runner, `rule-ids.json` and the generated traceability matrix with 66 of 66 engine-scope ids covered; `pnpm vectors` fails CI on a gap. `tools/vector-gen` regenerates the files. OQ-007 and OQ-008 logged. #2, #8 and #10 are deferred (ADR-0019).
- M0 #7 merged (12 Oct): redaction, auto-play, results, series and the module object.
- M0 #5 and #6 merged (12 Oct): tricks, thulla, pick-up, escaping, hand-taking, the endgame and the forced-outcome search.
- M0 #1, #3 and #4 merged (12 Oct): workspace and tooling, `@hulla/shared-ts`, the Thulla contract, card kit, rules schema, state and seeded deal.
- 12 Oct: the GDD is removed from the tracked tree and git-ignored; public documents cite it as a private document; README trimmed to an intro.
- 12 Oct: voice stays on Agora (O19 decided); billed minutes are cut by joining the channel on demand and leaving after silence.
- 12 Oct decisions: C# mirror deferred past v1.0 (ADR-0019), no clock in engines (ADR-0020), JSON Schema draft-07 (ADR-0021), one deck and 3–6 seats until v1.2, web client before Rung (M5 → web → Rung → M6, dates in the execution plan), Facebook login stays, Unity stays on 6000.4.9f1.

## Doing
- M2 #56 server and tooling part (branch `feat/content-pipeline`, stacked on #55): every content file carries id and version, `tools/content-sign` writes the manifest with hashes and CI fails on drift, `content_manifest` serves it signed with RSA-SHA256 when the box holds the key, `content_get` serves the bundled files, the Addressables groups are listed in PROJECT_STRUCTURE. The Unity ContentService with signature checks and cached Addressables is the Unity track. OQ-026 logged.
- M2 #55 server part (branch `feat/achievements`, stacked on #54): forty achievements in content with a schema, lifetime counters and streaks per player, evaluation at round end and on view, gems and titles through the ledger, the Houdini highlight proven through the real engine. The Profile panel is the Unity track. OQ-025 logged.
- M2 #54 server part (branch `feat/leaderboards`, stacked on #53): weekly and monthly Nakama leaderboards by Baithak points with the private-room daily cap, global and friends views, bracket rewards at reset through the ledger. The board screen is the Unity track. OQ-024 logged.
- M2 #53 server part (branch `feat/daily-reward`, stacked on the fixes): the 7-day calendar in content, a claim per local day through the ledger that is idempotent across devices, the cycle that continues after a missed day, and the sign-in notice. The Home card is the Unity track; the push hook waits for #46. OQ-023 logged.

## Next
Server: after #47 and #48 merge, #49 to #56, #71, #72, #75, #77. Unity: #33 polish, #23 theme from the design system (#22), #32 voice, the Profile screen for #40, the friends screen for #41, parties for Quick Play once friends exist.

## Blockers
- None.

## Decisions pending (owner)
- Repository secrets are not set (Unity licence, Android keystore, phase 0 SSH). See `docs/runbooks/github-and-ci-setup.md`.
- Phase 0 host: Oracle Always Free in London or Frankfurt if capacity is available; otherwise Azure free account or the owner's Mac behind Cloudflare Tunnel (TECH_STACK §6). Phase 1 region: one region only on open-source Nakama (ARCHITECTURE §8.13), chosen by #131.
- Trademark search and domain registration (#130): urgent, the repository is public and the GDD's name research was pushed in the first commit.
- History rewritten on 12 Oct: `main` is now 4835100 with no GDD. Still to do: untick "Allow force pushes" on the `main` rule, and ask GitHub support to purge the unreachable commit 116116e, which the site still serves by SHA. The ignore rule lands with the next commit.
- Product and legal, from the audit (GDD §31.2): O15 report weighting, O16 UK Online Safety Act (#133), O17 locked-screen voice on iOS, O18 the public-table endgame default, O20 wallet merge on account linking, O21 client nonces in the fairness commitment. Rule gaps OQ-001 and OQ-003 to OQ-006 run on interim choices until decided.
- Technical: Facebook login stays, so the install-size budget in GDD §19.5 is measured with the Meta SDK included at #35.
- Repository and planning, from the audit: a CLA or a "no external contributions" note, since PolyForm Noncommercial gives the owner no commercial rights over contributed code; the post-launch phases now run one at a time (web before Rung, decided), but the pre-launch estimate in GDD §29.3 omits deep links, deletion and export, the compliance pack and both UI passes, and 9 February 2027, the v1.0 date, is the first day of Ramadan: add the 20% buffer or move the soft launch to late January.

## How to update this file
Keep it under one screen. Move items between Done, Doing and Next; date the top line; never paste logs or chat here.
