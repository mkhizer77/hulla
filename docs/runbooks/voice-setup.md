# Voice setup

Table voice (GDD 11.4, #32) runs on our own LiveKit server behind `IVoiceService` (ADR-0012, ADR-0024). Nakama signs a room token for each player at a table; the Unity client (and later the web client) joins the table's room on the LiveKit server named in the token reply.

## How it behaves
- Each seat has a mic button beside Chat. Its state goes to the server (`set_mic`) and back to everyone in `table_state`.
- A client joins the table's room only while two mics at the table are on (its own counted), and leaves after two minutes in which nobody spoke. After that, the next mic change brings voice back (GDD 20.14, OQ-044). Spectators listen only: their token cannot publish.
- Private rooms default to open mic; public and ranked tables to push-to-talk, where a Hold to talk button appears once the room is open. A preset's `voiceDefault` overrides both; `off` hides voice.
- Only a seat with its mic on opens the microphone (and on iOS the call audio session); listeners and mic-off seats hear the table through Unity audio. The microphone records only after the player first sends; holding mutes the published track. Muting a player in the player sheet unsubscribes from their audio on this device. The mute list is on the server, so it holds after a reconnect.
- Leaving the table or being kicked removes the player from the voice room on the server side (`LIVEKIT_API_URL`); a seat that only dropped its connection mid-round keeps voice while the seat is held.
- A failed or dropped room never blocks play (E25): the pill says so and the mic button retries. The SDK reconnects by itself first.
- The server's `voice` switch in remote config hides voice everywhere.

## Local development
`docker compose up` in `server/nakama` starts LiveKit next to Nakama with the keys in `config/local.yml` and `infra/voice/livekit.local.yaml`. The editor uses the fake voice service; pass `-hulla-live-voice` to use LiveKit in the editor. Mac and phone builds use LiveKit.

For phones on the same Wi-Fi: start compose with `HULLA_BIND=0.0.0.0 HULLA_VOICE_IP=<the Mac's LAN IP>` and set `LIVEKIT_URL=ws://<the Mac's LAN IP>:7880` in `config/local.yml`.

`VoiceAgainstLiveKitTests` (Unity play mode, `HULLA_NAKAMA_HOST=127.0.0.1`) joins two seats to the local LiveKit and publishes both mics; the server's round integration test checks that LiveKit accepts the token. The first run on a Mac asks for microphone access.

## On a box
1. DNS: `voice.<domain>` through the proxy that already serves Nakama (WebSocket to port 7880), and `turn.<domain>` as a DNS-only record (not proxied) straight to the box.
2. Firewall: UDP 7882, TCP 7881, UDP 3478 and TCP 5349 (or 443 for TURN, see the config) open to the internet.
3. A certificate for `turn.<domain>` at `/etc/livekit/turn.crt` and `turn.key`.
4. Copy `infra/voice/livekit.production.yaml` to the box, fill the key, the secret (at least 32 characters, from `openssl rand -base64 36`) and the domain, and run `livekit/livekit-server` (the version pinned in `server/nakama/compose.yml`) with host networking and the file mounted at `/etc/livekit.yaml`.
5. Put these in Nakama's runtime env from the box's secrets file, then restart Nakama:

| Variable | What it is |
|---|---|
| `LIVEKIT_URL` | `wss://voice.<domain>`; the client gets it from `voice_token`. |
| `LIVEKIT_API_KEY` | The key name from the LiveKit config. |
| `LIVEKIT_API_SECRET` | The matching secret. It never leaves the server. |
| `LIVEKIT_API_URL` | Optional: where Nakama reaches LiveKit's API to remove players who leave, e.g. `http://127.0.0.1:7880` on the same box. Defaults to `LIVEKIT_URL` over HTTP. |

Without them `voice_token` answers `voice.unavailable` and the table shows that voice didn't connect.

Moving voice to another box or to LiveKit Cloud is only these three variables; clients need no update.

## Sizing
Audio only: a listener at a six-seat table receives at most five Opus streams of about 32 kbit/s, and silence sends almost nothing. One small box carries hundreds of concurrent tables; watch CPU and egress on the box dashboard. When one node is not enough, run more nodes with Redis (LiveKit's multi-node mode) in the same region.

## Checking it
1. Two phones and a third device at a private table on the phase 0 box, with the variables set.
2. One mic on: the pill says voice starts when another mic is on. A second mic on: both join and hear each other; rings light on the speaker's seat.
3. A public table: Hold to talk sends; letting go stops.
4. Mute a talking player from their badge: they go silent here. Turn the phone's network off and on: after the rejoin they are still silent.
5. A spectator hears the table without a microphone prompt; on iPhone, table sounds stay at full volume after leaving voice.
6. One phone on mobile data with UDP blocked (a strict hotspot or VPN): it still connects, over TCP or TURN.
7. Five players through a full round (#32 done-when).
8. Leave everyone quiet for two minutes: the pill says voice paused; turning a mic on brings it back.

## Not done yet
Per-seat volume, ducking the table sounds under speech, keeping voice for two minutes with the screen locked (VoIP background mode), Bluetooth and earpiece choice in settings, the under-16 voice limits (needs an age field at sign-up), and the web client's adapter (#109).
