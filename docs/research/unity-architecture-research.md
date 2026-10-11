# Unity client architecture: research notes (11 Oct 2026)

Scope that drives the choice: an app-like shell (about 25 screens; about 15 services: accounts, friends, presence, rooms, chat, voice, shop, pass, missions, settings, content, analytics) plus game modules rendered as light 2D tables driven by server events. Complex-script text (Urdu Nastaliq, Devanagari, Bengali). Low-end Android is a first-class target. One developer.

## Findings
| Topic | Finding | Source quality |
|---|---|---|
| Unity version | 6000.4.9f1 is the newest editor installed on the development Mac (Supported stream; 6.3 is the LTS line). A GameCI Linux image exists for it. | Local check; a prior project's CI |
| Complex-script text | UI Toolkit's Advanced Text Generator (HarfBuzz, ICU, FreeType) shapes Arabic-script and Indic text and handles RTL; opt-in, dynamic font assets only. TextMeshPro (inside uGUI 2.0) will not get it in Unity 6. | Unity docs and forum |
| UI binding | Unity 6 UI Toolkit has runtime data binding: `[CreateProperty]` on view-model properties and `INotifyBindablePropertyChanged` for change notification; Unity's own Unity 6 design-patterns course presents MVVM on this system. Missed notifications leave stale UI; event binding in UXML and richer ListView binding are roadmap items for 6.7 LTS. | Unity docs; roadmap post, Nov 2025 |
| Localisation | The Localization package binds `LocalizedString` to UI Toolkit elements in UXML or UI Builder (Unity 6+), with Smart Strings for plurals and placeholders. | Unity docs |
| Unity App UI | Unity's MVVM, navigation, DI and theming framework on UI Toolkit; still labelled experimental and provided as-is. Not a safe base for a shipping product. | Unity package docs |
| Dependency injection | Zenject/Extenject stale since 2020/2022. Reflex (14.3.1, June 2026) and VContainer (1.19, July 2026) are maintained; Reflex is lighter and faster; both scope per scene. Manual construction is clearer for small graphs; containers pay off for large service graphs. | 2026 comparison articles; project READMEs |
| Async | Unity 6 has built-in `Awaitable` (frame, time, background and main-thread helpers); Unity recommends it over Task. UniTask still offers richer combinators. Awaitables are pooled and must be awaited once. | Unity docs |
| Reactive | R3 succeeds UniRx and fits Unity 6, but has no UI Toolkit-specific binding; the built-in binding system covers the common cases. Optional. | Cysharp docs; community |
| Messaging | MessagePipe is built around DI containers; without one, plain C# events or a small typed event bus are simpler. | Cysharp docs |
| Assemblies | One asmdef per layer or feature with explicit references keeps boundaries honest and compile times low; pair feature assemblies with Addressables groups so a game module ships its assets separately. | Multiple guides |
| UI Toolkit on low-end Android | The uber shader is GPU-heavy on weak GPUs (Mali-T720 class); Unity 6.3 adds `UsageHint.LargePixelCoverage`; atlasing and usage hints control batches. Keep the table on sprites, HUD on UI Toolkit, profile on the reference device early. | Unity forum and docs |
| Client netcode pattern | Server emits typed events with sequence numbers; client applies in order, requests a snapshot on gaps, plays animations from a FIFO queue, locks input while busy; reconnect sends snapshot plus tail; timers are server deadline timestamps. | Community threads; matches GDD §9 |

## Options compared
| Option | Pros | Cons | Fit |
|---|---|---|---|
| UI Toolkit MVVM with runtime binding, hand-wired composition root, Awaitable, C# events | No third-party frameworks; standard Unity 6; readable; testable plain C# | Wiring code grows with services; binding discipline needed | Strong for one developer |
| Same plus Reflex | Less wiring; scoped lifetimes | One dependency; injection timing caveats around script execution order | Strong if the service graph keeps growing |
| VContainer + UniTask + MessagePipe + R3 | Most power and tooling | Most to learn and maintain | Weak for this scope |
| Unity App UI | Closest to an app framework | Experimental, as-is | Not now |

## Decision (11 Oct 2026)
UI Toolkit MVVM with runtime binding, a hand-wired composition root, Unity Awaitable and plain C# events. Recorded in ADR-0007, ADR-0008 and `docs/TECH_STACK.md`. Reflex is the fallback if hand wiring becomes a burden.
