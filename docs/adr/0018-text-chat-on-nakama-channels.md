# ADR-0018: Text chat, quick chat and emotes on Nakama channels and match data
Status: Accepted
Date: 2026-10-11

## Context
Tables need text chat, slang quick chat and emotes; clubs need persistent group chat later; everything must be moderated (filters for English, Roman Urdu and Hindi and the native scripts, rate limits, mute, block, reports, strikes) and must work in the Unity client and the web client at minimal cost.

## Decision
Table chat and club chat use Nakama's built-in realtime channels (a room channel per table, a group channel per club) with persistence and history. Quick chat and emotes travel as match data opcodes inside the table's match, which is cheap, ephemeral and needs no redaction. A server-side before-channel-message hook applies the multilingual filter, rate limits and mute and block checks before anything is delivered; filter lists are content, hot-reloadable without a release. Reports store the table id, round id, seat and the last seven days of that table's chat. No direct messages at launch (Nakama direct channels in v1.1). No third-party chat vendor.

## Consequences
Zero vendor cost and one user identity; moderation logic is ours and stays simple; chat UI is built in UI Toolkit now and React later; message retention is seven days for moderation and nothing longer.

## Alternatives considered
Stream or Sendbird (rich UI kits and dashboards, another SDK and identity, monthly fees past the startup tier); Firebase Firestore chat (second backend, filtering only through Cloud Functions, fan-out costs).
