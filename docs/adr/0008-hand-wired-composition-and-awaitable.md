# ADR-0008: Hand-wired composition root, Unity Awaitable and plain C# events
Status: Accepted
Date: 2026-10-11

## Context
Dependency inversion needs a composition root; async flows need cancellation tied to lifetimes; services need to notify each other. The client has about 15 services and one developer. Research compared hand wiring, Reflex, VContainer (with UniTask and MessagePipe) and Unity App UI: `docs/research/unity-architecture-research.md`.

## Decision
No dependency-injection container. Three hand-written scope classes (`AppScope`, `SessionScope`, `TableScope`) construct services in dependency order, expose interfaces and dispose their subtree. Constructor injection only; views receive view models through a `Bind` call. Unity's built-in `Awaitable` for asynchrony with cancellation tokens owned by scopes. Plain C# events, or a small typed event bus where many subscribers exist, for cross-service notifications. UI state flows through UI Toolkit runtime binding (ADR-0007). UniTask, MessagePipe and R3 are not added unless a measured need appears. Reflex is the recorded fallback container if hand wiring becomes a burden.

## Consequences
Zero framework dependencies and nothing to learn beyond Unity 6; every dependency is visible in a constructor; tests construct classes directly. Wiring code grows with the service count and must be kept tidy in the scope classes; developers must not reintroduce singletons or service locators (enforced by review and analyzers).

## Alternatives considered
Reflex (lightweight, maintained; kept as the fallback); VContainer with UniTask and MessagePipe (powerful, heavier, justified for larger teams); Zenject/Extenject (unmaintained since 2020/2022); service locator or singletons (hidden dependencies, untestable).
