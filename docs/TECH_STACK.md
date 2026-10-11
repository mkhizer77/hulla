# Hulla tech stack

> Version 1.0 · 11 October 2026 · Status: locked for v1.0. Changes need an ADR.
> Rules of the stack: spend nothing we do not have to until real players arrive, then pay for stability; share as much as possible between the Unity client and the later web client; put anything that might be swapped behind an interface.

## 1. At a glance
| Layer | Choice | Why |
|---|---|---|
| Mobile client | Unity 6000.4.9f1 (pinned), C#, IL2CPP, URP 2D, UI Toolkit with the Advanced Text Generator, Unity 6 runtime data binding, hand-wired composition root, `Awaitable`, LitMotion tweens, Addressables, Localization, Input System | Newest editor on the development machine; complex-script text; no framework dependencies (ADR-0007, ADR-0008, ADR-0016) |
| Web client (later) | TypeScript 5, React, PixiJS 8, Vite, `@heroiclabs/nakama-js`, Agora Web SDK, Firebase JS | Reuses the server's engine, protocol, content and tokens (ADR-0017) |
| Server | Nakama 3.x (open source) with Hulla runtime modules in TypeScript, Postgres 16, Docker Compose | Auth, social, matchmaking, wallets and storage built in; authoritative match handler is ours (ADR-0002) |
| Shared packages | JSON Schema protocol with generated TypeScript and C#; TypeScript rules engines (authoritative) with C# mirrors; shared JSON test vectors; content as signed JSON | One truth, two clients (ADR-0003, ADR-0005, ADR-0011) |
| Source and CI | Public GitHub repository, GitHub Actions, GameCI images, GitHub Container Registry | Free hosted runners including macOS on public repos (ADR-0015) |
| Hosting | Phase 0: free EU tier for development and testing; Phase 1: small paid boxes (EU for testers, South Asia for players); Phase 2: managed database and a second region | Cost follows users (ADR-0013) |
| Licence | PolyForm Noncommercial 1.0.0 | Public, readable, not clonable commercially (ADR-0014) |

## 2. Unity client
| Item | Locked choice | Version | Notes |
|---|---|---|---|
| Editor | Unity 6000.4.9f1 | pinned in `ProjectSettings/ProjectVersion.txt` | Supported stream; upgrade policy in ADR-0016 |
| Scripting | C# (Unity's compiler), IL2CPP, ARM64 + ARMv7 on Android, ARM64 on iOS | | `Nullable` on in all `Hulla.*` assemblies |
| Rendering | URP 2D Renderer | com.unity.render-pipelines.universal 17.4.x | Table is world-space sprites; UI is UI Toolkit |
| UI | UI Toolkit, Advanced Text Generator enabled at the root, runtime data binding | built in | MVVM without a library; USS tokens mirrored to web CSS |
| Text and fonts | Noto Nastaliq Urdu, Noto Sans Devanagari, Noto Sans Bengali, Noto Sans Gurmukhi, Noto Sans Arabic, Latin display and UI faces | dynamic font assets | Required by ATG |
| Localisation | com.unity.localization | 1.5.x | String tables, Smart Strings, UI Toolkit bindings, pseudo-localisation in CI |
| Content | com.unity.addressables | 2.x | Base content in the binary; cosmetics and later game modules streamed |
| Input | com.unity.inputsystem | 1.x | Touch first |
| Async | Unity `Awaitable` with cancellation tokens owned by scopes | built in | No UniTask |
| Composition | Hand-written `AppScope`, `SessionScope`, `TableScope` | our code | No DI container; Reflex is the recorded fallback |
| Events | Plain C# events; small typed event bus in `Hulla.Application` | our code | No MessagePipe |
| Tweens | LitMotion | latest stable | Allocation-free, MIT |
| Haptics | Nice Vibrations (open source) or a thin native bridge | | Decide at vertical slice |
| Networking | Nakama Unity client | OpenUPM `com.heroiclabs.nakama-unity`, latest stable | WebSocket, JSON |
| Voice | Agora Voice SDK for Unity behind `IVoiceService` | latest stable | Shared with web |
| Purchases | Unity IAP | latest stable | Store billing; receipts validated on the server |
| Ads | AppLovin MAX Unity plugin with AdMob, Meta, Unity Ads, Mintegral, ironSource, Liftoff adapters | latest stable | Rewarded and interstitial only; no banners |
| Analytics, crashes, config, push | Firebase Unity SDK: Analytics, Crashlytics, Remote Config, Cloud Messaging | latest stable | Free tier |
| Deep links | Branch Unity SDK | latest stable | Deferred deep linking; `hulla.gg/j/<code>` |
| Tests | Unity Test Framework (edit mode, play mode), NUnit for the pure C# packages | 1.6.x | Rule-ID traceability |
| Analyzers | Microsoft.Unity.Analyzers, Roslynator | | Warnings are errors in Domain and Application |

## 3. Server
| Item | Locked choice | Notes |
|---|---|---|
| Platform | Nakama 3.x open source, Docker image, one node | Clustering only if Heroic Cloud is ever bought |
| Runtime modules | TypeScript compiled to the ES level Nakama's JS runtime supports (official template settings), bundled with rollup or esbuild into one file | No Node APIs; synchronous match handlers |
| Database | Postgres 16 in Docker next to Nakama (Phase 0 and 1); managed Postgres in Phase 2 | Nightly `pg_dump` to object storage |
| Build tooling | Node 22 LTS, pnpm 9, TypeScript 5 strict, Vitest, fast-check, zod, ESLint flat config, Prettier, dependency-cruiser | |
| Fairness | Seed from four `nk.uuidv4()` values, xoshiro128** or PCG32 implemented identically in TS and C#, `nk.sha256Hash` commitment | ADR-0009 |
| Metrics and logs | Nakama Prometheus endpoint, structured JSON logs, Grafana Cloud free tier | |
| Admin | Nakama console plus admin RPCs | |

## 4. Multiplayer, in one paragraph
Every table is one authoritative Nakama match. Clients send intents (`play_card`, `take_hand`, `vote`, …) as JSON envelopes over Nakama's WebSocket; the match handler validates against the generated schema, applies the move through the pure rules engine, appends events to a sequenced log with a state hash, redacts the state per recipient and broadcasts. Timers are server deadlines; clients render countdowns from the server timestamp. A disconnected seat is covered by the deterministic auto-play policy for 30 seconds and reattached with a snapshot plus the missed events. Matchmaking uses Nakama's matchmaker with region, seat count, language and skill properties; private rooms use six-character codes mapped to match ids. Voice runs in a separate Agora channel per table. Text chat uses Nakama's realtime channels (one per table, one per club) behind a server-side filter hook; quick chat and emotes are match data opcodes (ADR-0018). No bots at launch. Details: `docs/ARCHITECTURE.md` §7 and §8.

## 5. Third parties
| Need | Provider | Plan and cost now | Unity | Web | Swap effort |
|---|---|---|---|---|---|
| Sign-in | Nakama auth with Apple, Google, Facebook, device accounts | Free | Yes | Yes | Low |
| Voice chat | Agora | 10,000 participant-minutes free per month, then about $0.99 per 1,000 | Yes | Yes | One adapter plus the token RPC |
| Text chat, quick chat, emotes | Nakama channels and match data, our own filters | Free | Yes | Yes | n/a |
| Multiplayer tables, matchmaking, friends, presence | Nakama (self-hosted) | The server box only | Yes | Yes | High (by design the one fixed piece) |
| Analytics, crash reporting, remote config, push | Firebase | Free tier | Yes | Yes (push via FCM web) | Medium |
| Ads mediation | AppLovin MAX | Revenue share | Yes | No ads on web at first | Medium |
| In-app purchases | Apple and Google through Unity IAP; Stripe on web later | Store fees; Stripe about 2.9% + fixed fee | Yes | Stripe | Medium |
| Deferred deep links | Branch | Free tier | Yes | Own router on `hulla.gg` | Low |
| DNS, TLS, proxy, WebSockets, tunnel | Cloudflare | Free plan | n/a | n/a | Low |
| Content and backups storage | Cloudflare R2 | 10 GB free, free egress | n/a | n/a | Low (S3 API) |
| Container images | GitHub Container Registry | Free for public repos | n/a | n/a | Low |
| CI | GitHub Actions with GameCI images | Free for public repos, including macOS runners | n/a | n/a | Medium |
| Monitoring | Grafana Cloud free tier, UptimeRobot free | Free | n/a | n/a | Low |
| Domains | `hulla.gg` (links), `hulla.games` (site) | About $30–60 a year | n/a | n/a | n/a |
| Store accounts | Apple Developer Program $99 a year; Google Play $25 once | | | | |

## 6. Hosting and cost plan (ADR-0013)
| Phase | When | Where | What runs | Monthly cost |
|---|---|---|---|---|
| **0: development and testing** | Now until external testers | A free EU tier close to the owner: Oracle Cloud Always Free (home region London or Frankfurt; ARM A1 up to 2 OCPU / 12 GB if capacity exists, otherwise two AMD Micro instances), with Azure's free account (12 months of a B2pts v2 or B1s in UK South or West Europe) as the fallback and AWS Free-plan credits (6 months) as a bridge; day-to-day development can also run on the owner's Mac behind Cloudflare Tunnel | Nakama + Postgres in Docker Compose, Cloudflare in front, R2 for content and dumps | $0 plus domains |
| **1: soft launch** | First external testers and the first player cohorts | Hetzner CX23 in Nuremberg or Falkenstein for EU/UK testers (about €6) and one player-region box: DigitalOcean Bangalore ($12–24) or Hetzner Singapore | Same compose stack per region; region-aware matchmaking | $20–50 |
| **2: growth** | Thousands of daily players | Bigger node per region, managed Postgres, Grafana Cloud paid tier if needed; Heroic Cloud only if operations should be bought | | $150–400 |
Everything is defined in `infra/` so a region is a copy of a compose file and a DNS record. Free tiers are for development; the first public cohort runs on a paid box.

## 7. Shared between Unity and web (ADR-0017)
| Shared | How |
|---|---|
| Wire protocol | JSON Schema source, generated TypeScript (web, server) and C# (Unity) |
| Rules engines | TypeScript engine runs on the server and in the browser; C# mirror in Unity; same vectors |
| Content | Presets, quick-chat packs, catalog, missions, strings: the same JSON files and manifest |
| Design tokens | USS variables in Unity mirrored to CSS variables on the web from one token file |
| Backend SDK | Nakama Unity and nakama-js against the same server and RPCs |
| Voice | Agora Unity and Web SDKs on the same channels |
| Analytics | Firebase on both, one event schema |
| Sign-in | The same Nakama auth providers |
| Links | One domain, one router |
| Not shared | Rendering (Unity versus PixiJS), purchases (stores versus Stripe), ads (mobile only), store packaging |
Unity WebGL was rejected for the web client: large downloads and memory limits on mobile browsers defeat the "join from a link in ten seconds" goal, and mobile browsers are not an officially supported Unity Web target.

## 8. Not used, and why
Unity Netcode or Multiplay (not needed for an authoritative turn-based server); Photon (real-time oriented, CCU pricing); PlayFab (second platform to run); Firebase Realtime Database for matches (weak for live tables); Vivox (no usable web SDK); Unity WebGL for the web client (see §7); DI containers, UniTask, MessagePipe, R3 (not needed at this scope); loot boxes (policy).

## 9. Versions and upgrades
Versions are pinned in `ProjectVersion.txt`, `Packages/manifest.json`, `package.json` and lockfiles. Unity is bumped only with a short ADR note and a green test run on the reference device. Third-party SDKs are bumped monthly in one pull request each, after reading release notes.
