# Hulla coding rules

> Version 1.0 · 11 October 2026 · Applies to every contributor in every package of this repository.
> Precedence when documents disagree: the GDD (private, held by the owner, not in this repository) for product behaviour → `docs/ARCHITECTURE.md` for structure → `docs/adr/` for recorded decisions → this document for conventions → code comments. Disagreements are logged in `docs/OPEN_QUESTIONS.md`, never resolved silently.

## 1. Golden rules
1. **The server is the truth.** No client code decides a game outcome, grants a reward or trusts another client.
2. **The domain is pure.** Rules engines and models have no framework types, no IO, no clock, no randomness except what is injected.
3. **Every rule has an ID and a test.** Code that implements GDD rule `R21` is covered by a test whose name contains `R21`. New behaviour without a rule ID gets one in the GDD or an open question first.
4. **Schema first.** Wire messages, content files and analytics events are defined in schemas before code; generated code is never edited by hand.
5. **Small, reviewable changes.** One concern per pull request, under 400 changed lines where possible, always green.
6. **Tests come with the change.** No behaviour change merges without tests; no test is weakened to make a build pass.
7. **No secrets, no PII, no hands in logs.** Ever.
8. **No hard-coded user-facing strings.** Every string is a localisation key.
9. **Data over code.** Presets, packs, catalog, missions and bot rosters are content; adding content never requires a client release.
10. **Boundaries are law.** The dependency rules in `docs/ARCHITECTURE.md` §5 and §9.1 are enforced by tooling; an exception needs an ADR.
11. **No wagering paths.** Nothing in the economy connects money to coins to game outcomes, and nothing lets players transfer currency.
12. **When the spec is silent, choose the conservative reading, implement it, and record the question.** Never block on it; never guess silently.

## 2. Repository conventions

### 2.1 Layout
See `docs/ARCHITECTURE.md` §5. New top-level folders need an ADR. Each package has a `README.md` with purpose, commands and ownership of boundaries.

### 2.2 Branching
Trunk-based: `main` is always releasable. Short-lived branches named `type/short-description` (`feat/thulla-endgame-draw`, `fix/table-rematch-vote`, `chore/codegen-drift-check`). Rebase on `main` before opening a pull request; no merge commits from `main` into branches.

### 2.3 Commits
Conventional Commits: `feat`, `fix`, `refactor`, `test`, `docs`, `chore`, `perf`, `build`, `ci`, with an optional scope (`feat(rules-engine): …`). Imperative mood, under 72 characters in the subject, body explains why. Reference rule IDs and open questions (`Implements R38–R41. See OQ-003.`).

**No attribution trailers or tool footers** in commits, pull requests or files.

### 2.4 Issues and pull requests
Issues are short: a one-line title, two to six lines of plain prose saying what and why, acceptance criteria as at most five bullets, links to the GDD rule IDs or ADRs instead of restating them. No walls of text, no exhaustive checklists, no generated-looking boilerplate. Pull requests follow the same tone.

Template sections: What and why; GDD and ADR references; Test evidence (commands run and results); Screenshots or recordings for UI; Risk and rollback; Checklist (§15). A pull request that touches `packages/protocol` or `content/` schemas must state compatibility impact. Draft until CI is green.

### 2.5 Definition of done
Code, tests, docs and content for the change are merged; CI green; traceability matrix updated when rules are touched; `docs/OPEN_QUESTIONS.md` updated when a gap was found; the GDD or an ADR updated when behaviour or structure changed; no TODOs without an issue link.

## 3. TypeScript rules (server, engines, shared, web)

### 3.1 Language and tooling
- TypeScript 5, `strict: true`, `noUncheckedIndexedAccess: true`, `exactOptionalPropertyTypes: true`, `noImplicitOverride: true`.
- ESLint flat config with `typescript-eslint` strict and stylistic presets, `eslint-plugin-import-x` ordering, `dependency-cruiser` for boundaries; Prettier with the repository config. Formatting is never discussed in review.
- Vitest for tests, fast-check for property tests.

### 3.2 Style and naming
- `camelCase` for values and functions, `PascalCase` for types, classes and enums, `UPPER_SNAKE_CASE` for true constants, kebab-case file names, one exported concept per file where practical.
- Named exports only; no default exports. Barrel files only at package roots.
- Prefer `readonly` arrays and properties; prefer `interface` for object shapes and discriminated unions for variants; `enum` only in generated protocol code, otherwise string literal unions.
- No `any`. `unknown` at boundaries, narrowed by zod validators.
- No `null` and `undefined` mixing: optional values are `T | undefined`; `null` only where an external schema demands it.
- Functions under 40 lines and files under 400 lines are the default; exceeding either needs a reason in the review.
- Comments explain why, never what; a comment that restates the code is deleted.

### 3.3 Domain purity (rules engines and shared)
- No imports from `node:*`, no `Date`, no `Math.random`, no `setTimeout`, no console. ESLint bans them in these packages.
- State is immutable: return new objects; use structural sharing; never mutate inputs.
- Randomness comes from the injected `Rng`; engines have no clock (ADR-0020).
- All public functions are total over their declared input types and return `Result` instead of throwing.
- Engines are versioned; a breaking rule change bumps the module version and keeps the previous engine available for the compatibility window.

### 3.4 Errors
- `Result<T, E>` from `packages/shared-ts`; `E` is an error code from the catalogue plus optional structured detail.
- Throw only for programmer errors (violated invariants). Nakama handlers catch at the boundary, log with correlation ids and return an error envelope.
- Never swallow errors; never log and continue without a decision.

### 3.5 Nakama runtime constraints
- Match handlers are synchronous; no `async`, no promises, no timers. Deadlines are ticks and timestamps checked in `matchLoop`.
- Only `nk` performs IO. Wrap `nk` in `server/nakama/src/infra` so services and the table core depend on narrow interfaces that tests can fake.
- Keep the bundle small and the transpile target at the level the runtime supports; verify with the local Nakama container on every change.
- Every inbound payload passes the generated zod validator before any logic runs.

### 3.6 Testing in TypeScript
- Unit tests next to the code in `__tests__/` folders; test names read as sentences with the rule ID first: `R20_thulla_ends_the_trick_immediately`.
- Property tests for invariants: card conservation, legal-only moves, termination, redaction, determinism (same seed and moves → same hash).
- Vectors run through `packages/test-vectors/runner-ts`; adding a rule means adding vectors.
- Snapshot tests are not used for behaviour; they are allowed only for generated code drift.

## 4. C# and Unity rules

### 4.1 Language and tooling
- Unity 6000.4.9f1 pinned in `ProjectVersion.txt` (ADR-0016); C# features as allowed by Unity's compiler; `Nullable` enabled in all `Hulla.*` assemblies.
- `.editorconfig` at the repository root is the formatter; `dotnet format` runs in CI for the pure packages.
- Analyzers: `Microsoft.Unity.Analyzers`, `Roslynator.Analyzers`; warnings are errors in `Hulla.Domain` and `Hulla.Application`.
- Tests: NUnit via `dotnet test` for `packages/rules-engine-cs`; Unity Test Framework for edit-mode and play-mode tests.

### 4.2 Style and naming
- `PascalCase` for types, methods, properties, events and public fields; `_camelCase` for private fields; `camelCase` for locals and parameters; interfaces start with `I`; async methods end with `Async`.
- One type per file; file name equals type name; folders mirror namespaces; namespaces start with `Hulla.`.
- Expression-bodied members for one-liners; braces always for multi-line blocks; `var` only when the type is obvious from the right-hand side.
- Prefer `readonly struct` and `record` types for values; prefer immutable collections exposed as `IReadOnlyList<T>`.

### 4.3 Architecture in Unity
- Assembly definitions per layer with explicit references (ARCHITECTURE §9.1); `Hulla.Domain` has `noEngineReferences: true`.
- Constructor injection, hand-wired in the scope classes of `Hulla.Composition` (ADR-0008). No service locators, no static mutable state, no singletons; `MonoBehaviour` views receive their view models through a `Bind` call from the owning scope.
- Logic lives in plain classes; `MonoBehaviour`s are thin views and adapters. No game logic in `Update`.
- Unity `Awaitable` for asynchrony with `CancellationToken`s tied to scope lifetime; never await the same `Awaitable` twice; no coroutines for logic; no `async void` except Unity event handlers that immediately delegate.
- Cross-service notifications through plain C# events or the small typed event bus in `Hulla.Application`; every subscription is disposed with its scope.
- `ScriptableObject`s only for authored data (layouts, themes, tutorial scripts), never for runtime state.
- Scenes are thin; everything is a prefab; scene merges are avoided by design.
- Addressables for all content that is not required at first frame; no `Resources/` folder.
- UI Toolkit for screens: one UXML per screen, styles only through USS tokens, no inline styles, runtime data binding to view models that derive from the shared `BindableObject` base and raise `propertyChanged` in every setter.

### 4.4 Performance
- No allocations per frame in the table loop: no LINQ, no boxing, no string concatenation, no closures capturing per frame; cache component references; pool everything that spawns more than once.
- `GetComponent`, `Find*`, `Camera.main` and `Resources.Load` never in hot paths.
- Profiler markers around scheduler steps and renderer work; a change that regresses the reference-device budget is rejected regardless of feature value.

### 4.5 Serialisation and protocol
- Only generated DTOs from `packages/protocol` cross the wire; mapping to domain types happens in `Hulla.Infrastructure`.
- No `JsonUtility` for protocol payloads; use the configured serializer with the generated converters.

### 4.6 Testing in C#
- Edit-mode tests for view models, `TableClient` ordering and `AnimationScheduler` timing; play-mode smoke per screen.
- Engine tests mirror the TS suite one to one and use the same vector files, once the C# mirror exists (ADR-0019).
- Test names carry rule IDs exactly like the TS suite.

## 5. Architecture boundaries and dependency rules
| Rule | Enforced by |
|---|---|
| `rules-engine` imports nothing from `server`, `protocol`, clients or Node | dependency-cruiser, ESLint restricted imports |
| `server` never imports from `apps/*` | dependency-cruiser |
| `Hulla.Domain` references no Unity or infrastructure assemblies | asmdef `noEngineReferences`, NetArchTest |
| `Hulla.Presentation` and `Hulla.Infrastructure` never reference each other | asmdef references, NetArchTest |
| Only `Hulla.Composition` references everything | asmdef references |
| Generated folders are read-only | CI drift check, CODEOWNERS |
Exceptions require an ADR and a reviewer's explicit approval in the pull request.

## 6. Adding or changing a game module
Checklist; every item is an acceptance criterion.
1. Rules specification in the GDD format with rule IDs, edge cases, toggles with defaults, auto-play policy and worked examples; owner sign-off.
2. Vectors in `packages/test-vectors/<game>/` covering every rule ID and edge case, including exhaustive enumeration of small endgames; traceability matrix regenerated.
3. TypeScript engine implementing the `GameModule` contract; purity lint clean; vectors green; property tests green.
4. C# engine mirroring the TS engine, vectors green, equivalence harness green over 10,000 random rounds, once the mirror exists (ADR-0019).
5. Server registration: rules schema and presets in `content/presets/<game>/`, module in the registry, redaction tests, auto-play tests, phase deadline policies.
6. Unity module: `ITableLayout`, `IStateRenderer`, `IInputController`, `IRulebook`, `ITutorial`, `IResultsAdapter`; default kit used wherever possible; edit-mode tests for input legality and renderer event coverage.
7. Content: quick-chat contexts, catalog tags, missions, string tables, bot roster entries if bots exist.
8. Feature flag with region and cohort filters; analytics events include `gameId`.
9. Documentation: module README, GDD appendix updated, open questions logged.
10. Soft launch behind the flag; retention and dispute data reviewed before the next wave starts.

## 7. Protocol and schema changes
- Schemas are the source of truth; change the schema, run `pnpm codegen`, commit the generated output, and never edit generated files.
- Within a major version only additive changes: new optional fields, new message types, new enum members with a documented default handling for unknown values.
- Breaking changes bump the major, keep the previous major served for the compatibility window, and come with a migration note in the ADR index.
- Every new message type has a validator, a round-trip test in TS and C#, and a redaction classification (public, seat-private, spectator-safe).

## 8. Content rules
- Every content type has a JSON Schema in `tools/content-schemas/`; `pnpm content:validate` runs in CI.
- Content is versioned and signed; the client refuses unsigned or downgraded manifests.
- Strings: stable ids, ICU message format limited to plural, select and simple arguments (exported to Unity Smart Strings by `tools/strings-export`), no concatenation, no text in images, pseudo-localisation must pass.
- Quick-chat packs follow the tone rules in GDD §11.2; a native speaker reviews every new pack.
- Presets never contain values outside the module's rules schema.

## 9. Testing rules
- The pyramid in `docs/ARCHITECTURE.md` §13 is the minimum; every change includes tests at the lowest level that proves it and, when it touches integration, at that level too.
- Determinism tests are mandatory for any code path that uses `Rng` or `Clock`.
- Flaky tests are quarantined within a day with an issue and fixed within a week; a quarantined test cannot be deleted.
- No sleeps in tests; use fake clocks and virtual time.
- Coverage gates: 95% lines in engines, 80% in application packages, and 100% of rule and edge IDs referenced by at least one test.

## 10. Performance rules
- Budgets in GDD §19.5 and §20.13 are acceptance criteria, measured on the reference device and the stage server.
- Measure before optimising; attach profiler captures or k6 reports to performance pull requests.
- Payload changes are reviewed for size; a message type exceeding 4 KB needs a reason.

## 11. Security and privacy rules
- Validate every input at the boundary; never trust client-supplied ids beyond what the session proves.
- Redaction tests accompany every new event, view or snapshot field.
- Secrets only in the host's secret manager; `.env` files are local, ignored and never committed; CI scans for secrets.
- No PII, hands, chat or voice content in logs, analytics or crash reports.
- Rate limits on every RPC and chat path; idempotency keys on every grant.
- Dependencies are pinned and scanned; a new runtime dependency needs a line in the pull request explaining why and its licence.
- Account deletion and data export flows are tested end to end before every release.

## 12. Logging, metrics and errors
- Structured JSON logs with `tableId`, `roundId`, `seat`, `seq`, `rpc`, `errorCode`; message text is a constant, variables are fields.
- Metrics follow `hulla_<area>_<name>_<unit>`; counters for events, histograms for durations.
- Error codes come from the catalogue; adding a code adds a localisation key and a test.

## 13. Documentation rules
- Architecture changes get an ADR before the code merges; superseded ADRs are marked, not deleted.
- Public APIs carry doc comments that state purpose, invariants and error codes; private code is documented only where the why is not obvious.
- Each package README has: purpose, commands, boundaries, how to test.
- When code deviates from the GDD, the GDD is updated in the same pull request or an open question is logged.

## 14. Contributor workflow
1. **Read order:** `docs/CONTEXT.md` → `CONTRIBUTING.md` → this document → `docs/ARCHITECTURE.md` → the GDD sections relevant to the task → the package README → the code.
2. **Plan before editing.** State the files to touch, the rule IDs involved and the tests to add; keep the plan in the pull request description.
3. **Work in small verifiable steps.** Implement, run the relevant tests and lints, then continue; never batch a day of changes into one unverified commit.
4. **Never weaken a test, delete a vector or relax a lint rule to go green.** Fix the code or raise an open question.
5. **Never edit generated code, lockfiles by hand, `ProjectSettings/` or `.meta` files without a stated reason.**
6. **No new dependencies without a one-line justification and licence check** in the pull request; no new frameworks without an ADR.
7. **Stay in scope.** Do not refactor unrelated code in a feature change; propose it separately.
8. **Ambiguity protocol:** choose the most conservative interpretation of the GDD, implement it, add a row to `docs/OPEN_QUESTIONS.md`, and reference the row in a code comment and the pull request.
9. **Cite rule IDs** in test names, commit bodies and comments so traceability is automatic.
10. **Run the full relevant suite before declaring done** (`pnpm test`, `pnpm lint`, `pnpm vectors`, `dotnet test`, Unity edit-mode tests for touched assemblies) and paste the results into the pull request.
11. **Report faithfully.** If something could not be verified, say so; if a step was skipped, say which and why.
12. **Never run destructive git or shell commands** (force pushes, history rewrites on shared branches, deleting branches or files outside the task) and never commit or push unless asked.
13. **Keep documentation in the same change** as the code it describes.
14. **No attribution lines** in commits, pull requests or files (see §2.3).
15. **Respect the data rules:** no real player data in fixtures; test accounts and seed content only.

## 15. Review checklist
- [ ] Behaviour matches the GDD rule IDs cited; new behaviour has an ID or an open question
- [ ] Tests added at the right level; names carry rule IDs; coverage gates hold
- [ ] Boundaries respected; no new cross-layer reference; purity lints clean
- [ ] Generated code regenerated, not edited; schemas versioned additively
- [ ] Redaction, idempotency and determinism considered for any server or engine change
- [ ] No hard-coded strings, no secrets, no PII in logs or analytics
- [ ] Performance budgets respected; hot paths allocation-free
- [ ] Localisation keys, accessibility and RTL considered for UI changes
- [ ] Documentation and ADRs updated; open questions logged
- [ ] Commit messages follow the convention; issues and pull requests are short and plainly worded

## 16. Prohibited patterns
`any`; default exports; `Math.random` or `Date` in domain code; `async` in Nakama match handlers; service locators, singletons and static mutable state in Unity; game logic in `MonoBehaviour.Update`; coroutines for logic; `Resources/`; `JsonUtility` for protocol payloads; LINQ or allocations in the table loop; hand-edited generated files; string concatenation for user text; `console.log` in server code; catching exceptions to ignore them; sleeps in tests; snapshot tests of behaviour; TODO without an issue; attribution trailers.

## 17. Quick reference
```
pnpm install                 # workspace install
pnpm lint                    # typecheck, eslint, prettier check, dependency-cruiser
pnpm typecheck               # tsc per package
pnpm format                  # prettier --write
pnpm test                    # vitest with coverage across TS packages
pnpm vectors                 # run shared vectors through the TS engine
pnpm codegen                 # regenerate protocol TS and C#; CI fails on drift
pnpm content:validate        # validate content against schemas
dotnet test packages/rules-engine-cs   # C# engine tests and vectors, when the mirror exists (ADR-0019)
pnpm equivalence -- --rounds 10000     # cross-engine harness, when the mirror exists (ADR-0019)
docker compose -f server/nakama/compose.yml up   # local Nakama and Postgres
```
Unity tests run from the Test Runner window or `-runTests -testPlatform EditMode` on CI.
