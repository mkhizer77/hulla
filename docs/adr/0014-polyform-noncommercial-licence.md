# ADR-0014: PolyForm Noncommercial 1.0.0 licence for the public repository
Status: Accepted
Date: 2026-10-11

## Context
The repository is public so that GitHub Actions, including macOS runners, are free and so the work can be shown. Hulla is a commercial product; a fully permissive licence would allow anyone to publish a clone on the stores.

## Decision
License the source under PolyForm Noncommercial 1.0.0 (text in `LICENSE`, required notice in `NOTICE` and the README). Anyone may read, run, modify and share the code for noncommercial purposes; commercial use requires written permission. Third-party assets, fonts, audio and SDKs keep their own licences and are listed where they are used. Contributions are accepted under the same licence.

## Consequences
Source-available rather than open source by the OSI definition; strong protection against commercial clones; the licence applies to all commits from the first public push, so it is set before the repository is created.

## Alternatives considered
MIT (used by the owner's other portfolio repositories; permits commercial clones); all rights reserved with visible source (maximum control, unclear permissions for readers).
