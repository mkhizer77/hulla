# Status

Updated: 12 October 2026 (afternoon)

## Phase
Pre-production. Design and architecture complete; no application code yet.

## Done
- GDD v1.3 (private, git-ignored at `docs/GDD_Hulla.md`): rules spec with IDs, platform catalogue, economy, UX, compliance, production plan.
- Architecture, tech stack, project structure, coding rules, contributing guide, 18 ADRs, research notes.
- Repository skeleton, CI workflow files, issue and PR templates, licence, M0 brief, GitHub and CI setup runbook.
- Public repository `mkhizer77/hulla` on GitHub. `main` protected: pull request required (no approvals, so the owner can merge), `ci` check required, linear history, squash merges only. Workflow token read-only. `phase0` environment with the owner as required reviewer.
- CI on GitHub: `detect` decides which parts exist, `ci` is the single required check, `release-android` calls `ci`, `nightly` runs the 10,000-round equivalence harness, secret scanning, Dependabot and CODEOWNERS in place, LFS cached, deploys pinned by commit SHA.
- Execution plan (`docs/EXECUTION_PLAN.md`): ten phases on GitHub milestones, 133 issues in execution order, design and asset track, web client after v1.0, two UI/UX passes.
- Audit of design, architecture and tech decisions (11 Oct): rule gaps logged as OQ-001 to OQ-006 with interim choices, document drift fixed, owner tickets #130 to #133 added, decisions below.
- 12 Oct: the GDD is removed from the tracked tree and git-ignored; public documents cite it as a private document; README trimmed to an intro.
- 12 Oct: voice stays on Agora (O19 decided); billed minutes are cut by joining the channel on demand and leaving after silence.
- 12 Oct decisions: C# mirror deferred past v1.0 (ADR-0019), no clock in engines (ADR-0020), JSON Schema draft-07 (ADR-0021), one deck and 3–6 seats until v1.2, web client before Rung (M5 → web → Rung → M6, dates in the execution plan), Facebook login stays, Unity stays on 6000.4.9f1.

## Doing
- M0 #1 (workspace and root tooling) and #3 (`@hulla/shared-ts`) are built and green locally: `pnpm lint`, `pnpm test` (39 tests, 98% lines), dependency-cruiser rejects Node built-ins from the engine. Waiting to be committed and opened as pull requests.

## Next
Work the M0 milestone issue by issue, starting with #1 (pnpm workspace and root tooling). Order and dependencies are in each issue; the map is `docs/EXECUTION_PLAN.md`, the brief is `docs/milestones/M0.md`. Open two pull requests: the audit and decision changes, then M0 #1 and #3. Next ticket: #4, the Thulla engine state, rules schema, presets and deal.

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
