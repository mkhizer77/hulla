# Event log

27 November 2026. What Hulla records about use, where each event goes and which question it answers. The source of truth for names and parameters is `packages/protocol/schemas/analytics/events.json`; codegen makes the C# builders and the TypeScript types from it (ARCHITECTURE §12.4). This page is the map. Decision record: ADR-0025.

## Rules

- Nothing is recorded before the player says yes to "Share anonymous usage data". A no, or switching it off later, drops what is queued, deletes the events waiting on the phone and throws away the analytics device id.
- No names, handles, chat or voice content, cards, seeds, birth year, phone number or email. The schema test, `AnalyticsClient.IsClean` on the client and `forbiddenParam` on the server each refuse them.
- No account id leaves the client. Amplitude sees a random id made on the device after consent; Firebase keeps its own instance id. Linking events to the account is one constructor argument (`AmplitudeSink(linkAccount: true)`) and a change to the privacy policy and the store labels first.
- No IP address is sent to Amplitude, so it does no location lookup; the country is the region set on the device.
- Every new event starts in the schema. A parameter is a string, number or flag, never a list or an object.

## Where events go

| Sink | Gets | For |
|---|---|---|
| Amplitude (client, HTTP V2 API, EU data centre) | Every client event except the per-move ones, plus what is known about the player | Funnels, retention, cohorts, the north star |
| Firebase Analytics (client, phones only) | Every client event | Crash context, install source, a second count |
| Server log (`analytics` lines from Nakama) | Table and RPC events with `gameId`, `tableId`, `roundId`, `seat`, `mode`; no player id | Operations and disputes; the Prometheus counters are made from the same events |

Events marked `x-high-volume` in the schema (a move, a pick-up, a thulla, a quick chat) happen dozens of times a round. They stay out of Amplitude, whose free plan is 2 million events a month; `round_end` carries their totals instead. At about 30 events a session and two sessions a day, that plan covers roughly 1,100 daily players.

The server's lines stay on the box (Docker's log, 30 days). Nothing ships them to a store that can be queried yet; when that is wanted, Loki next to Prometheus is the small step.

## Identity, sessions, delivery

- Device id: a random id in the app's preferences, per install.
- Session: starts with the first event and again after five minutes without one.
- Each event has an insert id, so a retry is never counted twice. Events wait in a file (500 at most) until Amplitude answers 200, so a round played offline is counted on the next launch. Ten events a request (the free plan's limit); a 429 waits 30 seconds; a dead network waits 5 seconds, then twice as long each time, up to 5 minutes.
- A build has Amplitude only when it was given a key (`docs/runbooks/amplitude-setup.md`). Without one nothing changes.

## On every event at a table

`game` (`thulla` or `rung`), added by the client while the player is at a table.

## What is known about the player

Sent to Amplitude as user properties, only when they change: `level`, `coins`, `gems`, `payer`, `remove_ads`, `friends` (a count), `rounds_on_device`, `locale`, `text_scale`, `reduce_motion`, `four_colour_deck`, `high_contrast`, `large_cards`, `left_handed`, `one_tap_play`, `voice_chat`. Device model, OS, app version and language go with each event.

## Events

C is the client, S the server log. A dash under Amplitude means the event is high volume or server only.

### Launch, account, navigation

| Event | Parameters | From | Amplitude | Answers |
|---|---|---|---|---|
| `app_open` | cold, deep_link_type | C | yes | Entry mix; the start of every funnel |
| `screen_view` | screen, overlay | C | yes | Where players go and where they stop |
| `sign_in` | method | C | yes | Guest or linked; once per account per launch |
| `account_link` | provider, outcome | C | yes | Linking funnel for Google, Facebook, Apple (phone: not yet) |
| `ftue_step` | step, outcome, duration | C | yes | Tutorial funnel |
| `error_shown` | code, screen | C | yes | Which refusals players actually see |
| `push_prompt` | answer | C | yes | Notification opt-in |
| `push_open` | route | C | yes | Which pushes bring players back |
| `perf_sample` | fps_avg, mem_mb, device_tier | C | yes | Frame rate by device, one per stretch in the foreground |

### Getting to a table

| Event | Parameters | From | Amplitude | Answers |
|---|---|---|---|---|
| `table_create` | mode, preset, seats, voice_mode | C, S | yes | Rooms made |
| `table_join` | via, mode, spectator | C | yes | How players reach a table: create, code, link, quick_play, ranked, rejoin, watch |
| `table_leave` | phase, reason, rounds, duration | C | yes | Evenings per table; who leaves mid-round |
| `invite_sent` | channel | C, S | yes | Invite mix |
| `invite_opened`, `invite_seated` | latency | C | yes | Invite funnel (GDD 23.2: opened to seated) |
| `match_search` | seats, wait_time, outcome | C | yes | Quick Play liquidity |
| `reconnect` | outcome, duration | C, S | yes | Reliability |
| `disconnect` | outcome, duration | S | — | Reliability, from the server's side |

### The round

| Event | Parameters | From | Amplitude | Answers |
|---|---|---|---|---|
| `round_start` | mode, seats, humans, bots, friends, rematch, preset_hash, region | C, S | yes | Volume by game and mode |
| `round_end` | duration, seats, humans, bots, friends, rank, lost, court, moves, timeouts, thullas_given, thullas_taken, cards_picked, chats, voice, bhabhi_cards | C, S | yes | The workhorse: friend rounds, round length, pace, voice use, who loses |
| `move_played` | legal_count, decision_ms, timeout, auto | C, S | — | Pace and AFK, move by move |
| `thulla`, `pickup`, `escape`, `hand_taken`, `empty_hand_draw`, `endgame_draw`, `confirmed` | counts and seats | C or S | — | Rule usage and game feel |
| `quick_chat`, `emote`, `chat_text` | pack, phrase_id, context, target | S | — | Which phrases are used |
| `voice_join` | mode | C | yes | Voice adoption |
| `voice_speak` | mode, duration | — | — | Not sent yet |
| `share_result` | ratio, sheet | C | yes | Share cards made |

`round_end` from the client is one player's view: `rank`, `lost`, `moves`, `timeouts`, `thullas_given`, `thullas_taken`, `cards_picked`, `chats` and `voice` are that player's own. A watcher sends the round without them. In Rung, `lost` is the pair's result and `court` marks a court.

### Friends and safety

| Event | Parameters | From | Amplitude | Answers |
|---|---|---|---|---|
| `friend_add` | source | C, S | yes | Where friendships start |
| `block`, `report` | source, reason | C, S | yes | Social health |

### Progress and money

| Event | Parameters | From | Amplitude | Answers |
|---|---|---|---|---|
| `level_up` | level | C | yes | Progress pace |
| `daily_reward_claim` | day, coins, gems | C | yes | Streak length |
| `mission_complete` | id | C, S | yes | Which missions get done |
| `achievement_unlock` | id | C | yes | Which achievements land |
| `pass_tier` | id, premium | C, S | yes | Pass progress |
| `shop_view` | item, placement | S | — | Catalog opens (the client's `screen_view` covers the screen) |
| `purchase` | item, price, currency, placement | C, S | yes | Coin and gem spending by item |
| `iap_start` | product | C | yes | Store sheet opened |
| `iap_result` | product, outcome, code | C | yes | Granted, cancelled or failed; granted means the server verified it |
| `ad_watch` | placement, rewarded | C | yes | Rewarded ads shown, and whether they paid |
| `ad_reward` | placement | C, S | yes | Rewards paid |
| `ad_impression` | placement | C | yes | Interstitials shown |

## Charts to build first

1. **Friend rounds per week** (north star, GDD 23.1): `round_end` where `friends` ≥ 1, per player per week.
2. **Retention** D1, D7, D30 on `app_open`, split by `game` of the first `round_end` and by first `table_join.via`.
3. **First session funnel**: `app_open` → `ftue_step` (last step) → `table_join` → `round_end`.
4. **Invite funnel**: `invite_sent` → `invite_opened` → `invite_seated` → `round_end`.
5. **Rounds per session** and **round length** from `round_end.duration`, by `game` and `mode`.
6. **Voice in private rooms**: share of `round_end` with `mode` private where `voice` is true.
7. **Store funnel**: `screen_view` shop → `iap_start` → `iap_result` granted; and `ad_watch` by `placement` and `rewarded`.
8. **Economy**: `coins` by `level`, `purchase` by `item`; shows when players run out of things to buy.
9. **Trouble**: `error_shown` by `code`, `table_leave` in phase `round`, `reconnect` gave_up, `perf_sample` by `device_tier`.

## Not recorded yet

- Linking a phone number (`account_link` has the value, nothing sends it).
- `voice_speak`, per-item `shop_view` on the client, and whether a rematch was voted (`round_start.rematch` says a round followed another at the same table).
- Revenue in money: `iap_result` has the product, not the price, since the store gives the client a formatted string. Use the stores' own reports for money.
- The update prompt and the maintenance notice.
- Server events in Amplitude. They would need the player's consent on the server and an id for the player; the client's events cover the same ground today.
