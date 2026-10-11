# ADR-0016: Unity 6000.4.9f1 pinned
Status: Accepted
Date: 2026-10-11

## Context
The owner asked for the newest Unity version installed on the development machine. Installed editors: 2021.3.x, 2022.3.x, 6000.4.6f1 and 6000.4.9f1. Unity 6.3 is the current LTS line; 6000.4 is the Supported (update) stream. A GameCI image exists for 6000.4.9f1.

## Decision
Pin Unity 6000.4.9f1 in `ProjectSettings/ProjectVersion.txt` and in the CI workflows. Packages use the latest stable releases compatible with 6000.4, pinned in `Packages/manifest.json`.

## Consequences
Newest fixes for UI Toolkit, the Advanced Text Generator and runtime data binding; the Supported stream moves faster than LTS, so an upgrade needs a green test run on the reference device and a one-line note here. Revisit if a blocking editor bug is fixed only on an LTS line.

## Alternatives considered
Unity 6.3 LTS (stable, but not installed and older for the features this project depends on).
