# Personal data inventory

What Hulla collects, where it lives, who gets it and how long it stays (#87, GDD 25.4). The privacy policy, the DPIA and the store data forms are written from this table; change it in the same pull request as any code that collects something new. Paths: **S** `server/nakama/src/`, **C** `apps/unity/Hulla/Assets/_Project/`.

| Data | Collected | Stored | Shared with | Gate | Retention |
|---|---|---|---|---|---|
| Device id (random GUID, the guest credential) | C `Infrastructure/Nakama/NakamaSessionService.cs` | Nakama user device | — | needed | with the account |
| Display name, handle, bio, avatar, frame, flag, title, privacy toggles, stats, friend code | profile screens | `profiles`, `friend_codes` (S `services/profiles.ts`) | other players see the public card | bio and flag optional | with the account; the friend code is freed |
| Birth year | first launch (C `Composition/AppFlow.Age.cs`) | `age/birth_year`, server-only, set once (S `services/age.ts`, `age_set`) | — | needed (age gate) | with the account |
| Friends, favourites, recent players, mutes, blocks | Friends | Nakama friends, `favourites`, `recent_players`, `mutes`, `blocks` | — | optional | with the account; the id stays in other players' lists until they change |
| Presence and last seen | sign-in, `presence_set` | `presence` | friends | `privacy.showOnline` | with the account |
| Phone number | Phone sign-in | only `ph_` + SHA-256(pepper:number) as the custom id (S `services/phone_otp.ts`) | Meta WhatsApp Cloud API, Twilio Verify | optional | unlinked with the account |
| One-time codes | the same | hash of the code or the provider's reference, 10-minute life | Meta, Twilio | — | 1 day (`admin_retention_purge`) |
| IP address | `phone_start` | rate counter keyed by IP in `phone_otp` | — | — | 1 day (`admin_retention_purge`) |
| Contacts' phone numbers | Find friends, opt-in | hashed and matched on the server, not stored (S `services/suggestions.ts`) | — | OS permission and in-app opt-in | not kept |
| Facebook friend ids | Find friends, opt-in | matched, not stored; linking Facebook imports friends who play into the friend graph | Meta | opt-in | friend graph until deletion |
| Social sign-in ids | linking | Nakama `apple_id`, `google_id`, `facebook_id` | Apple, Google, Meta | optional | with the account |
| Text chat (filtered, 140 characters) | table and lobby | `chat_log` per table (S `services/reports.ts`) | the table | free text locked for guests and under-16s with strangers | 7 days (`admin_chat_logs_purge`); copies in reports |
| Quick chat and emotes | table | round log only (phrase id) | the table | — | with the round log |
| Voice | LiveKit, mic opt-in | not recorded; self-hosted LiveKit with no egress (`infra/voice`) | the table | mic permission; under-16s only with friends | none |
| Round logs: seats, seed, moves, timings | server | `round_logs`, `round_results` | — | — | 180 days (`admin_retention_purge`); the user id becomes "deleted" at account purge |
| Purchases | Unity IAP | `entitlements`, `ledger_keys` (S `services/purchases.ts`) | Apple and Google receipt validation | optional | with the account |
| Wallet ledger | server | Nakama wallet ledger | — | — | with the account |
| Ads | MAX after the first round | `ad_rewards` with the time-zone offset | AppLovin and its networks: advertising id, device data, Hulla user id | MAX consent flow and ATT; under-18s get no personalised ads | with the account |
| Usage analytics (18 events, no user id) | C `Application/Analytics` | — | Firebase Analytics | `AnalyticsConsent` | Firebase setting |
| Crash reports | Crashlytics | — | Firebase | `AnalyticsConsent` | Firebase setting |
| Push device record (token, locale, time zone) | C `Application/Social/PushRegistration.cs` | `push` (S `services/push.ts`) | Firebase Cloud Messaging; a push carries the sender's display name | OS notification permission | with the account; a token is dropped after 60 days unused |
| Reports | `report_create` | `reports`, `report_index`: reporter, target, reason, table, 7 days of chat, the target's public card | — | — | 1 year (`admin_retention_purge`) |
| Moderation: mutes, strikes, bans | admin | `moderation` | — | — | with the account |
| Device bans | admin | `device_bans` (random device GUID) | — | — | until expiry |
| Admin audit log | admin RPCs | `audit` | — | — | kept: it records staff actions |
| Look feedback | after a round in a test look | `look_feedback` | — | optional | with the account |
| Server logs and metrics | server | stdout, Prometheus | — | — | logs 30 days (box config, #76); metrics 35 days |

The links site (`infra/cloudflare/links`) sets no cookies and runs no analytics; Cloudflare keeps its own edge logs.

## Consent before collection

Firebase collection, the advertising id and Meta's automatic events are off at build time (`BuildTools/Editor/FirebaseConsentDefaults.cs`, `BuildTools/Editor/SocialSetup.cs`) and turn on only with "Share anonymous usage data". The FCM token is fetched on Android before that question because push needs it; it is covered by the notification permission. MAX starts after the first live round, with its consent flow and, on iOS, the tracking prompt.

## Export and deletion

Settings > Account exports the account (storage, wallet, friends, chat, profile) through the share sheet and deletes it after 14 days (S `services/account_deletion.ts`). The purge erases the account, its push record and its owned storage, frees the friend code and replaces the user id in round logs. A player without the app asks through the link domain's `/delete` page or the support address. The scheduled purges (`admin_purge_deleted`, `admin_chat_logs_purge`, `admin_retention_purge`) run from the box's cron sidecar (#76).
