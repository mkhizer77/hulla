# Project structure

> Version 1.0 · 11 October 2026. How the repository, the Unity project and the server are laid out, and the naming rules that keep them that way. Boundaries are explained in `docs/ARCHITECTURE.md` §5 and §9.1.

## 1. Repository
```
hulla/
├─ README.md  CONTRIBUTING.md  CODING_RULES.md  LICENSE  NOTICE  .gitignore
├─ package.json  pnpm-workspace.yaml  pnpm-lock.yaml  tsconfig.base.json  vitest.config.ts
├─ eslint.config.js  .prettierrc.json  .dependency-cruiser.cjs  .editorconfig  .node-version  .gitattributes
├─ .github/
│  ├─ workflows/            ci.yml · nightly.yml · release-android.yml · server-image.yml
│  ├─ ISSUE_TEMPLATE/       task.md · bug.md
│  ├─ PULL_REQUEST_TEMPLATE.md
│  ├─ CODEOWNERS            generated folders, workflows and ADRs need the owner's review
│  └─ dependabot.yml        monthly bumps for actions, npm and NuGet
├─ apps/
│  ├─ unity/Hulla/          the Unity project (see §2)
│  └─ web/                  TypeScript web client, later (React + PixiJS + Vite)
├─ server/nakama/           runtime modules, config, Dockerfile, compose (see §3)
├─ packages/
│  ├─ protocol/             schemas/ (source of truth) · generated/ts · generated/csharp (a UPM package Unity references locally) · codegen scripts
│  ├─ rules-engine/         TypeScript engines: src/<game>/… · src/contract.ts · src/card-kit/
│  ├─ rules-engine-cs/      [LATER, ADR-0019] C# mirrors: Hulla.Domain/ (UPM package root: package.json · Runtime/ · the .csproj) · Hulla.Domain.Tests/ (NUnit, outside the package root)
│  ├─ shared-ts/            Result, error catalogue, ids, PRNG, hashing
│  └─ test-vectors/         <game>/*.json · schema/vector.schema.json · traceability.md · runner-ts/ · runner-cs/ (later)
├─ content/                 presets/ quick-chat/ catalog/ missions/ strings/ tokens/ bots/  (JSON, validated in CI)
├─ infra/                   compose/ (local, phase0, phase1) · cloudflare/ · scripts/ · dashboards/
├─ tools/                   codegen/ · content-schemas/ · content-validate/ · content-sign/ · strings-export/ · tokens-export/ · vector-gen/ · replay/ · equivalence/
└─ docs/                    GDD_Hulla.md (private, git-ignored) · ARCHITECTURE.md · TECH_STACK.md · PROJECT_STRUCTURE.md · CONTEXT.md · STATUS.md · OPEN_QUESTIONS.md · adr/ · research/ · runbooks/
```
Rules: new top-level folders need an ADR; every package has a `README.md` (purpose, commands, boundaries); generated folders are never edited by hand; content files carry a `version` and a `schema` field.

## 2. Unity project (`apps/unity/Hulla`)
```
Assets/
├─ _Project/
│  ├─ Composition/           Hulla.Composition.asmdef · AppScope.cs · SessionScope.cs · TableScope.cs · Bootstrap scene script
│  ├─ Domain/                Hulla.Domain.asmdef · models, policies, Result (noEngineReferences; the engine mirror joins as a local package later, ADR-0019)
│  ├─ Application/           Hulla.Application.asmdef · Services/ (interfaces) · UseCases/ · ViewModels/ · Table/ (TableClient, AnimationScheduler, EventQueue) · Events/ (typed bus)
│  ├─ Infrastructure/        Hulla.Infrastructure.asmdef · Nakama/ · Voice/ · Iap/ · Ads/ · Firebase/ · Content/ · Localization/ · Save/ · DeepLinks/
│  ├─ Presentation/          Hulla.Presentation.asmdef · Screens/<ScreenName>/ (UXML, USS, View, bindings) · TableKit/ (seat ring, hand fan, pile, timer ring, stamps) · Theme/ (tokens.uss, fonts, icons) · Navigation/
│  ├─ Games/
│  │  └─ Thulla/             Hulla.Games.Thulla.asmdef · Layout · Renderer · Input · Rulebook · Tutorial · Results · Addressables group "game-thulla"
│  └─ Tests/                 EditMode/ and PlayMode/ per assembly (Hulla.Tests.*.asmdef)
├─ AddressableAssetsData/    groups: base, ui-theme-default, cards-classic, cards-desi, tables-*, avatars-*, emotes-*, vo-*, game-thulla
├─ Scenes/                   Boot.unity (composition root, loads Main) · Main.unity (UI shell) · Table.unity (additive, per table)
├─ Settings/                 URP assets, UI Toolkit panel settings, Localization settings
└─ Plugins/                  third-party SDKs that are not UPM packages (MAX, Branch)
Packages/
├─ manifest.json             pinned versions; local reference to ../../../../packages/protocol/generated/csharp (Hulla.Protocol), so nothing generated is copied; ../../../../packages/rules-engine-cs/Hulla.Domain joins when the mirror is built (ADR-0019)
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
├─ cron/                     the sidecar's schedule: nightly aggregation, log purge, reminders (calls admin RPCs)
├─ compose.yml               nakama + postgres + cron sidecar for local development; deployment overlays live in infra/compose/
└─ README.md
```
Game rules never live here; the match handler imports them from `packages/rules-engine`.

## 4. Packages
- `packages/rules-engine/src/<game>/`: `state.ts`, `rules.ts` (toggle schema and presets), `moves.ts`, `engine.ts` (the `GameModule` implementation), `redact.ts`, `autoplay.ts`, `events.ts`, `__tests__/` with rule-ID test names.
- `packages/rules-engine-cs/Hulla.Domain/` [LATER, ADR-0019]: the UPM package Unity references, with `package.json`, `Runtime/Hulla.Domain.asmdef` and one folder per game under `Runtime/<Game>/`; the `.csproj` for `dotnet` sits beside them with build output redirected outside the package through `Directory.Build.props`, because Unity imports every file under a local package. `Hulla.Domain.Tests/` is a separate NUnit project outside the package root and runs the vectors and property tests under `dotnet test`.
- `packages/protocol/schemas/`: `envelope.json`, `shell/`, `games/<game>/`, `analytics/`; `generated/ts` and `generated/csharp` are produced by `pnpm codegen` and committed; `generated/csharp` carries a `package.json` and `Hulla.Protocol.asmdef` so Unity references it as a local package instead of a copy.
- `packages/test-vectors/<game>/`: `V-###-<slug>.json`; `traceability.md` is generated and maps every rule and edge ID to vector IDs. Each vector carries a full explicit state (every hand, the discard, the leader, the open trick), never a partial setup that an engine must complete, and uses the fixed assertion vocabulary of `schema/vector.schema.json`, which CI validates.

## 5. Content
`content/<type>/<name>.json` with a `schema` field pointing to `tools/content-schemas/<type>.schema.json`. Strings: `content/strings/<locale>.json` exported to the Unity tables and to the web (ADR-0023). Quick-chat packs: `content/quick-chat/<pack>.<locale>.json`. Presets: `content/presets/<game>/<preset>.json`. Strings are ICU message format limited to plural, select and simple arguments; `tools/strings-export` (`pnpm codegen`) writes the Unity tables (Smart Strings, `Localization/Resources/Strings`) and `packages/strings` for the web. `content/tokens/tokens.json` is the single design-token source; `tools/tokens-export` writes `tokens.uss` and `tokens.css`.

`tools/content-sign` writes `content/manifest.json` (every file with its type, id, version, SHA-256 and size; the version moves when any file changes) and CI fails on drift; the server serves it through `content_manifest`, with the RSA-SHA256 signature made at deploy time (`pnpm content:sign`, set on the box as `HULLA_CONTENT_SIGNATURE`), and the files' exact text through `content_get`. In Unity, `Infrastructure/Content/ContentStore` ships the manifest and files in StreamingAssets, checks the signature against the public key in `Composition/ContentKey`, downloads changed files after sign-in, caches them by hash under the persistent data path, and pins one manifest per launch; offline it keeps the cache, and a damaged cache falls back to the bundle (ADR-0011).

Addressables groups (art and audio referenced by the catalog, never in the base scene): `ui-core` (icons, fonts, the stamp set), `game-thulla` (table layout, pile, card animations), `cards-classic` and `cards-desi` (faces and the twelve backs), `tables` (surfaces), `avatars` (avatars and frames), `emotes` (emote packs), `audio-sfx` and `audio-music`, `vo-<pack>` (voice-over packs), `seasons-<name>` (season themes). Each group streams on demand and is cached; `ui-core`, `game-thulla` and `avatars-base` (the faces every seat can show) ship in the binary. `BuildTools/Editor/AddressablesSetup` (menu Hulla > Set up Addressables) creates the groups; the bundles build with the player. Remote bundles are built against the placeholder host `https://cdn.invalid/hulla`, which `Infrastructure/Content/AddressablePacks` rewrites to remote config's `content_cdn_url`; while that is empty, remote packs stay off.

## 6. Branches, tags, environments
`main` is releasable. Branches `feat/…`, `fix/…`, `chore/…`. Tags `v<major>.<minor>.<patch>` trigger release builds; `server-v…` tags mark server deploys. Environments: `local` (compose), `phase0` (free EU box), `phase1-eu`, `phase1-asia`.

## 7. Where things do not go
No secrets anywhere in the tree (`.env` is ignored); no binaries except small UI sprites and fonts (large art through Addressables and Git LFS); no generated code outside `generated/`; no game logic in `apps/` or `server/`.
