# ADR-0017: Shared stack between the Unity and web clients
Status: Accepted
Date: 2026-10-11

## Context
A web client follows the mobile launch so friends can join from a link without installing anything. Building it twice must not double the work or the cost.

## Decision
Share everything that is not rendering: the JSON Schema protocol with generated TypeScript and C#; the TypeScript rules engine (server and browser) with its C# mirror and common vectors; content files and the string tables; design tokens (USS and CSS from one file); the Nakama SDKs against the same server; Agora voice on both; Firebase analytics with one event schema; the same sign-in providers; one link domain and router. Rendering differs by platform (Unity versus React and PixiJS), as do purchases (stores versus Stripe) and ads (mobile only). Unity WebGL was rejected for the web client because of download size and memory limits on mobile browsers, which are not an officially supported Unity Web target.

## Consequences
The web client is mostly a presentation layer over shared packages; provider choices (LiveKit since ADR-0024, Firebase, Nakama) were made with both platforms in mind; the token and content pipelines must export to both formats from day one.

## Alternatives considered
Unity WebGL for the web (maximum code sharing, poor mobile-browser experience); a fully separate web stack (duplicated rules and protocol work).
