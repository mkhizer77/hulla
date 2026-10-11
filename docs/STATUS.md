# Status

Updated: 11 October 2026

## Phase
Pre-production. Design and architecture complete; no application code yet.

## Done
- GDD v1.2 (`docs/GDD_Hulla.md`): rules spec with IDs, platform catalogue, economy, UX, compliance, production plan.
- Architecture, tech stack, project structure, coding rules, contributing guide, 17 ADRs, research notes.
- Repository skeleton, CI workflow files, issue and PR templates, licence, M0 brief, GitHub and CI setup runbook.

## Doing
- Nothing in progress.

## Next (milestone M0, see `docs/milestones/M0.md`)
1. Create the GitHub repository (public), push the skeleton, enable branch protection with the `ci` check required.
2. `pnpm` workspace, TypeScript config, lint, Vitest; `.NET` solution for `rules-engine-cs`.
3. Thulla rules engine in TypeScript, then the C# mirror, driven by shared vectors (GDD §6, Appendix C) with full rule-ID traceability.
4. Cross-engine equivalence harness; redaction and auto-play tests.
5. Protocol schemas and codegen for the Thulla messages.
6. Nakama local compose; table core skeleton; first end-to-end round with two scripted clients.

## Blockers
- None.

## Decisions pending (owner)
- Phase 0 host: Oracle Always Free in London or Frankfurt if capacity is available; otherwise Azure free account or the owner's Mac behind Cloudflare Tunnel (TECH_STACK §6).
- Trademark search for "Hulla" (classes 9 and 41) and domain registration (hulla.gg, hulla.games, hulla.co).

## How to update this file
Keep it under one screen. Move items between Done, Doing and Next; date the top line; never paste logs or chat here.
