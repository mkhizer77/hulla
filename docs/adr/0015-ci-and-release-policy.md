# ADR-0015: CI and release policy: tests gate merges, tag-triggered Android builds
Status: Accepted
Date: 2026-10-11

## Context
The project lives on GitHub. Builds must be reproducible and automatic, tests for key features must pass before anything merges, and the owner wants to start with Android releases and add iOS later.

## Decision
- `ci.yml` runs on every pull request and on `main`: lint, TypeScript tests, C# tests, shared vectors in both engines, codegen drift, content validation, dependency rules and Unity edit-mode tests (GameCI). It is a required status check; `main` is protected and squash-merged.
- `release-android.yml` runs when a `v*` tag is pushed: the same tests, then an Android build (AAB and APK) through GameCI's Unity builder, attached to a GitHub Release. Store upload stays manual until the Play account and signing keys exist.
- iOS builds join the release workflow when the Apple developer account exists; public repositories make macOS runners free.
- `server-image.yml` builds the Nakama bundle and image on `main`, pushes it to GitHub Container Registry, and deploys only through a protected environment with manual approval.
- Issues and pull requests stay short and plainly worded; the templates in `.github/` enforce the shape.

## Consequences
No build without green tests; releases are reproducible from a tag; one Unity licence activation file per editor version is stored as a secret; Android first keeps the pipeline small.

## Alternatives considered
Building on every push (wasteful); private repository with hosted macOS minutes (cost); building iOS from day one (no account yet).
