# ADR-0007: UI Toolkit with runtime data binding for screens, world-space sprites for the table
Status: Accepted
Date: 2026-10-11

## Context
The product must render Nastaliq Urdu, Devanagari and Bengali correctly and mirror layouts for RTL, while animating cards at 60 fps on low-end Android. It is an app-like shell with about 25 screens, so screen construction and state binding dominate the UI work. Research: `docs/research/unity-architecture-research.md`.

## Decision
UI Toolkit for all screens and the HUD, with the Advanced Text Generator enabled at the root for complex-script shaping and RTL, and Unity 6 runtime data binding as the MVVM mechanism (view models expose `[CreateProperty]` properties and raise `propertyChanged`; UXML declares data sources and binding paths; the Localization package binds strings). The table itself is a world-space 2D sprite layer with pooled card views and a tween library, driven by the animation scheduler.

## Consequences
Correct text in every target language from day one; MVVM without a third-party library; cheap, precise card animation; two UI technologies separated by a clear boundary (HUD versus table). Binding discipline is required: a setter that forgets to notify leaves stale UI, so view models use a shared base class that raises notifications. Some binding conveniences (event binding in UXML, richer ListView binding) arrive in later Unity 6 releases; the shell works around them with small helper components.

## Alternatives considered
uGUI with TextMeshPro everywhere (no complex-script shaping in Unity 6, plugins required and still weak for Nastaliq); UI Toolkit for the table too (weaker animation tooling and batching for many moving sprites); Unity App UI framework (experimental, as-is).
