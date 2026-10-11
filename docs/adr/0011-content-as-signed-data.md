# ADR-0011: Content is signed data delivered through a manifest and Addressables
Status: Accepted
Date: 2026-10-11

## Context
Presets, quick-chat packs, cosmetics, missions, strings and bot rosters change often, per region and per season, and new game modules should not bloat the base install.

## Decision
All such content is JSON validated against schemas in CI, versioned, signed and published to a CDN with a manifest. Art and audio ship as Addressables bundles referenced by the catalog. Clients verify signatures, pin versions per session and fall back to cache offline. Base content ships in the binary.

## Consequences
Live operations without releases; small installs; a signing key to manage; a content pipeline to maintain.

## Alternatives considered
Content in code (releases for every change); unsigned remote content (tampering risk).
