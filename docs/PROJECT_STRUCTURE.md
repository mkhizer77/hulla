# Project structure

> Version 1.0 · 11 October 2026. How the repository, the Unity project and the server are laid out, and the naming rules that keep them that way. Boundaries are explained in `docs/ARCHITECTURE.md` §5 and §9.1.

## 1. Repository
```
hulla/
├─ README.md  CONTRIBUTING.md  CODING_RULES.md  LICENSE  NOTICE  .gitignore
├─ package.json  pnpm-workspace.yaml  pnpm-lock.yaml  tsconfig.base.json  .editorconfig
├─ .github/
│  ├─ workflows/            ci.yml · release-android.yml · server-image.yml
│  ├─ ISSUE_TEMPLATE/       task.md · bug.md
│  └─ PULL_REQUEST_TEMPLATE.md
├─ apps/
│  ├─ unity/Hulla/          the Unity project (see §2)
│  └─ web/                  TypeScript web client, later (React + PixiJS + Vite)
├─ server/nakama/           runtime modules, config, Dockerfile, compose (see §3)
├─ packages/
│  ├─ protocol/             schemas/ (source of truth) · generated/ts · generated/csharp · codegen scripts
│  ├─ rules-engine/         TypeScript engines: src/<game>/… · src/contract.ts · src/card-kit/
│  ├─ rules-engine-cs/      C# mirrors: Hulla.Domain.<Game>/ · Hulla.Domain.Tests/ · a UPM package.json so Unity can reference it
│  ├─ shared-ts/            Result, error catalogue, ids, PRNG, hashing
│  └─ test-vectors/         <game>/*.json · traceability.md · runner-ts/ · runner-cs/
├─ content/                 presets/ quick-chat/ catalog/ missions/ strings/ bots/  (JSON, validated in CI)
├─ infra/                   compose/ (local, phase0, phase1) · cloudflare/ · scripts/ · dashboards/
├─ tools/                   codegen/ · content-schemas/ · content-validate/ · vector-gen/ · replay/ · equivalence/
└─ docs/                    GDD_Hulla.md · ARCHITECTURE.md · TECH_STACK.md · PROJECT_STRUCTURE.md · CONTEXT.md · STATUS.md · OPEN_QUESTIONS.md · adr/ · research/ · runbooks/
```
Rules: new top-level folders need an ADR; every package has a `README.md` (purpose, commands, boundaries); generated folders are never edited by hand; content files carry a `version` and a `schema` field.

## 2. Unity project (`apps/unity/Hulla`)
```
Assets/
├─ _Project/
│  ├─ Composition/           Hulla.Composition.asmdef · AppScope.cs · SessionScope.cs · TableScope.cs · Bootstrap scene script
│  ├─ Application/           Hulla.Application.asmdef · Services/ (interfaces) · UseCases/ · ViewModels/ · Table/ (TableClient, AnimationScheduler, EventQueue) · Events/ (typed bus)
│  ├─ Infrastructure/        Hulla.Infrastructure.asmdef · Nakama/ · Voice/ · Iap/ · Ads/ · Firebase/ · Content/ · Localization/ · Save/ · DeepLinks/
│  ├─ Presentation/          Hulla.Presentation.asmdef · Screens/<ScreenName>/ (UXML, USS, View, bindings) · TableKit/ (seat ring, hand fan, pile, timer ring, stamps) · Theme/ (tokens.uss, fonts, icons) · Navigation/
│  ├─ Games/
│  │  └─ Thulla/             Hulla.Games.Thulla.asmdef · Layout · Renderer · Input · Rulebook · Tutorial · Results · Addressables group "game-thulla"
│  ├─ Protocol/              Hulla.Protocol.asmdef · generated C# DTOs (copied by codegen, read-only)
│  └─ Tests/                 EditMode/ and PlayMode/ per assembly (Hulla.Tests.*.asmdef)
├─ AddressableAssetsData/    groups: base, ui-theme-default, cards-classic, cards-desi, tables-*, avatars-*, emotes-*, vo-*, game-thulla
├─ Scenes/                   Boot.unity (composition root, loads Main) · Main.unity (UI shell) · Table.unity (additive, per table)
├─ Settings/                 URP assets, UI Toolkit panel settings, Localization settings
└─ Plugins/                  third-party SDKs that are not UPM packages (MAX, Branch, Agora)
Packages/
├─ manifest.json             pinned versions; local reference to ../../../../packages/rules-engine-cs (Hulla.Domain)
└─ packages-lock.json
ProjectSettings/             ProjectVersion.txt is the single source of the Unity version
```
Assemblies and references are listed in `docs/ARCHITECTURE.md` §9.1. Naming: one type per file, file name equals type name, folders mirror namespaces (`Hulla.Presentation.Screens.Lobby`). UXML and USS files share the screen's name (`LobbyScreen.uxml`, `LobbyScreen.uss`). Addressables addresses follow `<group>/<name>` (`cards-desi/back-truck-art`). Scenes are thin; everything is a prefab or a UXML document. No `Resources/` folder.

## 3. Server (`server/nakama`)
```
server/nakama/
├─ src/
│  ├─ main.ts                InitModule: registers RPCs, hooks, match handlers
│  ├─ rpc/                   table_create.ts · table_join_by_code.ts · presets.ts · voice_token.ts · content_manifest.ts · economy_*.ts · missions_*.ts · profile_*.ts · report_create.ts · account_delete.ts · admin_*.ts
│  ├─ hooks/                 after_authenticate.ts · before_channel_message.ts · purchase_validated.ts · ad_reward_s2s.ts
│  ├─ match/                 table_core.ts (lifecycle, pipeline, timers, log, snapshots, redaction dispatch) · registry.ts (game id → module)
│  ├─ services/              wallet.ts · inventory.ts · progression.ts · missions.ts · leaderboards.ts · reports.ts · flags.ts · content.ts
│  ├─ infra/                 nk.ts (narrow wrappers) · logger.ts · metrics.ts · rng.ts · hash.ts
│  └─ __tests__/             unit tests with a fake nk
├─ build/index.js            bundled output (ignored by git, built in CI)
├─ config/                   local.yml · phase0.yml · phase1.yml
├─ Dockerfile                nakama image plus the bundle
├─ compose.yml               nakama + postgres for local and phase 0
└─ README.md
```
Game rules never live here; the match handler imports them from `packages/rules-engine`.

## 4. Packages
- `packages/rules-engine/src/<game>/`: `state.ts`, `rules.ts` (toggle schema and presets), `moves.ts`, `engine.ts` (the `GameModule` implementation), `redact.ts`, `autoplay.ts`, `events.ts`, `__tests__/` with rule-ID test names.
- `packages/rules-engine-cs/Hulla.Domain.<Game>/`: the same files as C# types; `Hulla.Domain.Tests/` runs the vectors and property tests under `dotnet test`.
- `packages/protocol/schemas/`: `envelope.json`, `shell/`, `games/<game>/`, `analytics/`; `generated/` is produced by `pnpm codegen` and committed.
- `packages/test-vectors/<game>/`: `V-###-<slug>.json`; `traceability.md` is generated and maps every rule and edge ID to vector IDs.

## 5. Content
`content/<type>/<name>.json` with a `schema` field pointing to `tools/content-schemas/<type>.schema.json`. Strings: `content/strings/<locale>.json` exported to the Unity Localization tables and to the web. Quick-chat packs: `content/quick-chat/<pack>.<locale>.json`. Presets: `content/presets/<game>/<preset>.json`.

## 6. Branches, tags, environments
`main` is releasable. Branches `feat/…`, `fix/…`, `chore/…`. Tags `v<major>.<minor>.<patch>` trigger release builds; `server-v…` tags mark server deploys. Environments: `local` (compose), `phase0` (free EU box), `phase1-eu`, `phase1-asia`.

## 7. Where things do not go
No secrets anywhere in the tree (`.env` is ignored); no binaries except small UI sprites and fonts (large art through Addressables and Git LFS); no generated code outside `generated/`; no game logic in `apps/` or `server/`.
