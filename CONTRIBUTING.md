# Contributing to Hulla

This guide is for anyone contributing to **Hulla**, a multiplayer platform for the games South Asian friends play in person (Thulla/Bhabhi first, Rung next, then Ludo, Tambola, Antakshari and more). Unity 6 mobile client first, a TypeScript web client later, one authoritative Nakama server in between. Read `docs/CONTEXT.md` first, then this file, then `CODING_RULES.md`, then `docs/ARCHITECTURE.md`, then the GDD sections your task touches. The GDD is private and not in this repository; the section numbers and rule IDs cited across these documents refer to it, so ask the owner for access before taking a rules task.

## Non-negotiables
- The server is the only authority over game state. Clients render, hint and predict cosmetically.
- Rules engines are pure and deterministic: injected RNG, no clock (ADR-0020), immutable state, `Result` instead of exceptions.
- Every GDD rule ID (`R1`…, `E1`…) has at least one test whose name contains it in the TypeScript engine (and in the C# mirror once it exists, ADR-0019).
- Schema first: protocol, content and analytics are defined in schemas; generated code is never hand-edited.
- No wagering, no currency transfers, no loot boxes, anywhere in the code.
- No hard-coded user-facing strings, no secrets, no PII or hands in logs.
- Bots and offline play are deferred (GDD D7). Absences are covered by the deterministic auto-play policy (R44). Do not build bots unless the task says so.
- Never add attribution trailers or "generated with" lines to commits, pull requests or files.
- Never commit or push unless explicitly asked; never run destructive git commands.

## Where things live
| Need | Path |
|---|---|
| Product behaviour and rules | The GDD, a private document held by the owner (§6 rules, §6.13 toggles, §6.15 edge cases, Appendix C vector format); not in this repository |
| Structure and boundaries | `docs/ARCHITECTURE.md` (§5 packages, §7 module contract, §8 server, §9 Unity) |
| Why decisions were made | `docs/adr/` |
| Conventions and prohibited patterns | `CODING_RULES.md` |
| Questions the spec does not answer | `docs/OPEN_QUESTIONS.md` (add a row; do not guess silently) |
| Current state and next steps | `docs/STATUS.md` (update it when you finish a work session) |
| Protocol schemas and generated code | `packages/protocol/` |
| TypeScript engines (authoritative) | `packages/rules-engine/` |
| C# engines (client mirror, deferred by ADR-0019) | `packages/rules-engine-cs/` |
| Shared vectors and traceability | `packages/test-vectors/` |
| Server runtime | `server/nakama/` |
| Unity client | `apps/unity/Hulla/` |
| Content (presets, packs, catalog, strings) | `content/` |

## Workflow for every change
1. **Understand.** Identify the GDD sections and rule IDs involved. If the task conflicts with the GDD or the architecture, stop and say so before coding.
2. **Plan.** List the files you will touch, the tests you will add and any schema or content changes. Keep it short and put it in the pull request description. Issues and pull requests stay small and plainly worded (see `CODING_RULES.md` §2.4).
3. **Implement in small steps.** After each step run the narrowest relevant checks (a test file, a lint). Keep functions small, names clear, boundaries intact.
4. **Test.** Add tests at the lowest level that proves the behaviour; name them with rule IDs; run the full relevant suite (see commands) before you declare the task done.
5. **Document.** Update the package README, the GDD or an ADR in the same change when behaviour or structure changed. Log open questions.
6. **Report.** Summarise what changed, what you verified (with the exact commands and results), and what you could not verify. Never claim a passing suite you did not run.

## Commands
```
pnpm install
pnpm lint                 # typecheck, eslint, prettier check, dependency-cruiser
pnpm test                 # vitest with coverage
pnpm typecheck
pnpm format
pnpm vectors
pnpm codegen
pnpm content:validate
pnpm equivalence -- --rounds 1000        # when the C# mirror exists (ADR-0019); nightly uses 10000
dotnet test packages/rules-engine-cs           # when the C# mirror exists (ADR-0019)
docker compose -f server/nakama/compose.yml up
```
Unity: open `apps/unity/Hulla` in the pinned Unity version (6000.4.9f1, ADR-0016); run edit-mode tests from the Test Runner for the assemblies you touched.

## Tests gate merges
`main` is protected. The `ci` workflow (lint, TypeScript and C# tests, shared vectors, codegen drift, content validation, Unity edit-mode tests) must be green before a pull request can merge. Release builds run only from `v*` tags and run the same tests first (ADR-0015).

## Releasing
Push a tag `vX.Y.Z` on `main`. The release workflow builds the Android AAB and APK and attaches them to a GitHub Release. Store upload is manual for now; iOS joins the workflow when the Apple account exists.

## Definition of done
Code, tests, docs and content merged together; CI green; traceability matrix updated when rules were touched; open questions logged; no TODO without an issue; pull request follows the template and the review checklist in `CODING_RULES.md` §15.

## When the spec is unclear
Prefer the conservative reading of the GDD, implement it, record an open question with your interim choice, and point to it from a comment. Ask the owner only when proceeding under any assumption would waste the work.

## Current phase
See `docs/STATUS.md`. Pre-production. The next engineering milestone is **M0**: repository tooling, the Thulla rules engine in TypeScript and shared vectors with full rule traceability; the C# mirror and the equivalence harness are deferred (ADR-0019). Nothing in `apps/` or `server/` exists yet beyond placeholders; create structure according to `docs/ARCHITECTURE.md` §5 and §9.1.
