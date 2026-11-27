# ADR-0025: Amplitude for product analytics, over its HTTP API

Date: 27 November 2026. Status: accepted. Adds to the analytics rows of TECH_STACK; Firebase stays.

## Context
Firebase Analytics has been the only place client events go (#71). It is free and already wired, but funnels, retention by cohort and "players who did A then B" take BigQuery and SQL there, and its reports lag by a day. The questions before soft launch are of exactly that kind: friend rounds per week (GDD 23.1), the invite funnel, first-session drop-off, which game keeps players.

The legal drafts promise that usage data carries no account id and is collected only after consent (docs/legal).

Options:

| Option | Cost | Client weight | Notes |
|---|---|---|---|
| Firebase alone, with BigQuery export | Free at this size | None added | Every funnel is a query to write and keep |
| Amplitude, Unity SDK | Free to 2 million events a month | Native libraries for iOS and Android, nothing on the Mac QA windows | One more SDK to keep in step with Unity and the stores |
| Amplitude, HTTP V2 API from our own sink | The same | About 400 lines of C#, no native code | Works on every Unity target and later in the web client; we own batching, retries and the offline file |
| PostHog or Mixpanel | Similar free tiers | Similar | No reason to prefer over Amplitude; the owner asked for Amplitude |

## Decision
- Client events also go to Amplitude, through `AmplitudeSink` behind the existing `IAnalyticsSink`: plain HTTPS posts to the HTTP V2 API, no SDK. `AmplitudeQueue` (pure C#, tested) builds the events, sessions, batches and retries.
- The EU data centre (`api.eu.amplitude.com`) by default, next to the Frankfurt box; a project in the US region is a build setting.
- Same consent as Firebase: nothing before the yes; a no deletes the waiting events and the device id.
- No account id and no IP address are sent. The device id is random and per install. Linking to the account is a constructor argument, to be turned on only after the privacy text changes.
- Per-move events (`x-high-volume` in the schema) stay out of Amplitude; `round_end` carries the round's totals. That keeps a day's play at about 60 events a player, so the free plan lasts to roughly 1,100 daily players.
- The key comes into a build from `HULLA_AMPLITUDE_KEY` or the git-ignored `HullaAnalytics.local.json`. A build without a key has no Amplitude and behaves as before.
- Firebase Analytics keeps receiving every event: it costs nothing, feeds Crashlytics' breadcrumbs and gives a second count to check against.
- The server keeps writing its events as log lines. Sending them to Amplitude would need the player's consent on the server and a player id in the event; the client's events answer the same questions for now.

The event list and what each answers: `docs/design/analytics-events.md`.

## Consequences
- One more processor in the privacy policy, the data inventory, the store forms and the consent question (all updated with this change); a data processing addendum with Amplitude to accept (#87).
- A player on two phones counts as two until the account is linked; an account switch on one phone stays one device.
- No money figures in Amplitude: the client knows a product's formatted price only. Revenue stays in the stores' reports.
- Past the free plan the choice is paying, sampling `screen_view`, or moving the sink to another vendor; only `AmplitudeSink` and `AmplitudeQueue` know Amplitude.

## Alternatives considered
See the table. The SDK would add sessions and device details for free, but also native builds to maintain for a few hundred lines of logic we can test ourselves.
