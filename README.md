# Hulla

> The games friends play when they are in the same room, rebuilt so the same group gets the same evening from anywhere. First game: Thulla (Bhabhi). Then Rung, Ludo, Tambola, Antakshari and the rest of the catalogue.

**Status:** pre-production. Design is complete; implementation starts with milestone M0 (rules engines and shared test vectors).

## Where things are
| Path | What |
|---|---|
| `docs/ARCHITECTURE.md` | The platform architecture: layers, module contract, server, Unity client, protocol, testing, operations |
| `docs/adr/` | Architecture decision records (why we chose what we chose) |
| `CODING_RULES.md` | The rulebook every contributor follows |
| `CONTRIBUTING.md` | Read order, commands, workflow, definition of done |
| `docs/CONTEXT.md`, `docs/STATUS.md` | One-page context and the current state, kept short so a new contributor starts from files, not history |
| `docs/TECH_STACK.md` | The locked tech stack, third parties, hosting and cost plan |
| `docs/PROJECT_STRUCTURE.md` | How the repository and the Unity project are laid out |
| `docs/research/` | Short research notes behind the main decisions |
| `docs/milestones/` | One brief per milestone with deliverables and acceptance (M0 first) |
| `docs/runbooks/` | Operational how-tos (GitHub and CI setup first) |
| `docs/OPEN_QUESTIONS.md` | Spec ambiguities recorded during implementation, for the owner to decide |
| `docs/GDD_Hulla.md` | The game design and production document (source of truth for product and rules) |
| `apps/unity/` | Unity 6 LTS client (shell plus game modules) |
| `apps/web/` | TypeScript web client (after mobile launch) |
| `server/nakama/` | Nakama runtime modules (TypeScript), config and container build |
| `packages/protocol/` | JSON Schema source of truth for the wire protocol, with generated TypeScript and C# |
| `packages/rules-engine/` | Pure TypeScript rules engines, one per game (authoritative on the server, reused on the web) |
| `packages/rules-engine-cs/` | Pure C# mirrors of the engines for the Unity client |
| `packages/test-vectors/` | Shared JSON test vectors both engines must pass |
| `content/` | Presets, quick-chat packs, catalog, missions, strings, bot rosters: signed data, not code |
| `infra/` | Deployment, monitoring and local development stack |
| `tools/` | Codegen, content validation, vector generation, replay |

## Licence
PolyForm Noncommercial 1.0.0. Read, run, modify and share for noncommercial purposes; commercial use needs written permission. See `LICENSE` and `NOTICE`.

## Principles in one breath
The server is the truth. The domain is pure. Games are modules; the shell is generic. Content is data. Every rule has an ID and a test. Nobody wagers anything, ever.

## Getting started
See `CONTRIBUTING.md` for the command reference once M0 lands. Until then, this repository holds design documents only.
