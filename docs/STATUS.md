# Status

Updated: 13 October 2026 (night)

## Phase
M0 complete: the Thulla rulebook runs in TypeScript with full traceability. M1, the vertical slice, starts with the protocol schemas.

## Done
- M2 #41 server part merged (13 Oct, PR 169): add by friend code with the 500 cap, ten pinned favourites, Recent Players written at round end; requests and blocks ride Nakama's friend API. OQ-016 logged.
- M2 #38 server part merged (13 Oct, PR 168): guest merge by one-time code, one active table session per account with resume-token takeover; provider linking is the client's call once the owner configures the providers. OQ-015 logged.
- M1 #17 to #20, M2 #39 and #40 merged (13 Oct): room codes with region and expiry, `table_join_by_code`, `presets_get`, `voice_token` with a pure-TypeScript Agora token builder (OQ-011); the matchmaker hook with public tables that auto-start, `seat.party` and `table_state.autoStart`; the scripted reconnection suite; quick chat, emotes and table text with the before-message hook (OQ-012); profiles, handles, friend codes and the stats card (OQ-013); account deletion with a 14-day grace period, export and the scheduled purge, with a Nakama paging quirk guarded everywhere (OQ-014). Unity #29 Quick Play merged (PR 167).
- M1 #16 merged (13 Oct): the Thulla host, named match callbacks, the game registry, `table_create`, and the scripted four-seat round against Nakama in Docker. Nakama facts learned and recorded (ADR-0022, ARCHITECTURE §8.1, server README): handlers resolve by global name, the globals freeze after InitModule, the match state is rebuilt as plain data between callbacks, and a message target needs Nakama's node. Server-side validation moved from zod to compiled checks. A native Nakama plus Postgres runs on the dev Mac for server tickets.
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
- M2 #42 server part (branch `feat/presence`): presence states from Nakama's online flag, the seat pointer the match binding writes, the client's Away flag and the live match label; `table_list_friends` ordered for the Home hero; `presence_set`. The Home screen itself, the hero and the offline variant are the Unity track; the client polls or follows status for the 5 s refresh. OQ-017 logged.

## Next
Server: after #38, #41 and #42 merge, #45, #47 to #56, #71, #72, #75, #77. Unity: #33 polish, #23 theme from the design system (#22), #32 voice, the Profile screen for #40, the friends screen for #41, parties for Quick Play once friends exist.

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
