# Amplitude

Product analytics for the client (ADR-0025). The events and what they answer: `docs/design/analytics-events.md`. Until a build is given a key, Amplitude is off and nothing else changes.

## The account

Made on 27 November 2026: an organisation in the EU region on the free plan (`app.eu.amplitude.com`, the owner's personal sign-in) with two projects, **Hulla** (id 100072950, store builds) and **Hulla dev** (id 100073074, development builds). The steps, for a new organisation:

1. Sign up at amplitude.com on the Starter (free) plan. When asked for the data region choose **EU**; it cannot be changed later for a project.
2. Make two projects: `Hulla` for store builds and `Hulla dev` for development builds, so test rounds stay out of the real numbers.
3. In each project: Settings → Projects → the project → copy the **API key**. It is a write-only key that ships inside the app, so it is not a secret, but it stays out of the repository like the Firebase files.
4. In the organisation's settings accept the data processing addendum (#87).
5. Set the projects' data retention to 13 months (GDD 23.6). If the plan does not offer the setting, change the retention line for Amplitude in `docs/legal/privacy.md` to what the plan keeps.

## The key in a build

Either in the environment of the build: `HULLA_AMPLITUDE_KEY` holds the project's API key, and `HULLA_AMPLITUDE_REGION` is `us` for a project in the US region (leave it unset for the EU).

Or in the git-ignored `apps/unity/Hulla/HullaAnalytics.local.json` in the checkout that builds, with two fields: `amplitudeKey` (the API key) and `amplitudeRegion` (`eu` or `us`).

`BuildCommands` writes it next to the server address for the build only and logs "build with Amplitude (eu)" or "build without Amplitude". Use the `Hulla dev` key for development builds and the `Hulla` key for `AndroidRelease` and `ios-release.sh`. CI builds have no key until `HULLA_AMPLITUDE_KEY` is added as a secret to the release job.

A development build also takes `-hulla-amplitude-key <key>` on the command line (the Mac QA windows).

## Checks

Quick, without a build: the play-mode test `AmplitudeAgainstAmplitudeTests` sends three events and a player description through the real sink and passes when Amplitude answers 200. Run it with `HULLA_AMPLITUDE_KEY` set to the dev project's key; the project's settings page then shows three more events. It is skipped without the key.

With a build:

1. Start a development build with a key, answer **Yes, share** to the usage question, open a tab or two and play a round against bots.
2. Development builds log each batch (`[analytics] …`) and any answer from Amplitude other than 200 (`amplitude: 400 …`).
3. In Amplitude → the dev project → User Look-Up, the device appears within a minute with `app_open`, `screen_view`, `table_join`, `round_start`, `round_end`. Check that `round_end` has `game`, `friends` and `voice`, that no `move_played` arrives, and that the user has no User ID.
4. Switch "Share anonymous usage data" off in Settings, play another round, and confirm nothing new arrives; switch it on again and confirm the device shows up as a new user (the old id was thrown away).
5. Airplane mode for a round, then back online: the round's events arrive with their original times.

## Limits to know

- Free plan: 2 million events a month, 10 events a request. The client sends ten at a time and keeps up to 500 on the phone when offline.
- Per-move events never go to Amplitude; see the design page before adding an event that fires more than a few times a round.
- A 400 from Amplitude drops the batch (it would be refused again). A wrong key shows up as `amplitude: 400` in a development build's log.
