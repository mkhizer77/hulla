# ADR-0012: Voice provider behind an interface, Agora first
Status: Accepted; the provider choice is superseded by ADR-0024 (self-hosted LiveKit), the interface stands
Date: 2026-10-11

## Context
Voice is core to the product and must work in the Unity client now and in the TypeScript web client later, sharing as much of the stack as possible at minimal cost. Vivox is Unity-native and free to 5,000 peak concurrent users but offers no usable browser SDK (Unity WebGL only, limited, with a separate non-public web SDK). Agora provides Unity and web SDKs, 10,000 free minutes a month and about $0.99 per 1,000 minutes after that. LiveKit also offers both SDKs.

## Decision
All voice functionality sits behind `IVoiceService` in the Unity client and an equivalent interface in the web client. Agora is the first adapter on both platforms so the same voice channels serve mobile and browser players. Pricing is reviewed when monthly voice minutes approach the free allowance; Vivox (mobile-only) and LiveKit are the recorded fallbacks.

## Consequences
One voice provider across clients; low cost at launch; a provider swap touches one adapter and the token RPC, not the product. Agora minutes are counted per participant, so a four-seat table consumes four minutes per minute of talk; the GDD's cost model is updated accordingly.

## Alternatives considered
Vivox everywhere (cheapest at scale, no web); LiveKit (viable, smaller free tier); building on raw WebRTC (far too much work for one developer).
