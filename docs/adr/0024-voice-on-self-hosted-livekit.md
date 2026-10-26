# ADR-0024: Voice on self-hosted LiveKit

Date: 26 October 2026. Status: accepted. Replaces the provider choice in ADR-0012; the interface stays.

## Context
Table voice has to work on Android, iOS and the web client, cost little at launch and stay cheap as tables grow. ADR-0012 picked Agora: 10,000 free participant-minutes a month, then about $0.99 per 1,000. Billing per participant-minute means a four-seat table costs four minutes for every minute of talk, and the soft-launch scenario in TECH_STACK §5 comes to roughly $1,500 to $2,400 a month before the on-demand join cuts it. The Agora Unity SDK also had to be downloaded and imported by hand, so the adapter in PR 271 was never compiled.

LiveKit is an open-source (Apache 2.0) WebRTC media server (SFU) with a production Unity SDK (2.x, iOS, Android, macOS, Windows, on OpenUPM) and a web SDK (`livekit-client`). Run on our own box it costs only the box and its traffic. Audio is small: Opus at about 32 kbit/s, silence suppressed, so a listener at a six-seat table receives at most about 160 kbit/s and usually far less. LiveKit Cloud runs the same server with the same tokens and SDKs, which gives a managed fallback without client changes.

Options compared for six-seat audio tables on Unity mobile and the web:

| Option | Cost at scale | Unity mobile | Web | Reliability | Notes |
|---|---|---|---|---|---|
| Agora | about $0.99 per 1,000 participant-minutes | Yes, manual SDK import | Yes | Managed, global | Cost grows with seats times minutes |
| LiveKit Cloud | Free tier, then paid plans by usage | Yes | Yes | Managed | Same SDK and tokens as self-hosted |
| LiveKit self-hosted | The box and its traffic | Yes | Yes | Ours to run; built-in TURN on 443 | Scales out with Redis |
| Vivox | Free to 5,000 peak users | Yes | No usable web SDK | Managed | Fails the web requirement |
| Peer-to-peer WebRTC over Nakama signalling | Only TURN traffic | Unity WebRTC package, no echo cancellation for the Unity mic | Yes | Weak on mobile NATs, five uploads per phone | Cheapest on paper, not reliable enough |

## Decision
- Voice runs on LiveKit, self-hosted next to Nakama on the same region's box. `IVoiceService` stays the only seam: `LiveKitVoiceService` in Unity, the same SDK family on the web.
- `voice_token` signs a LiveKit access token (HS256 JWT) with the server's API key and secret: the identity is the Nakama user id, the room is the match id (a table code can come back for a new table), seats may publish the microphone only, spectators may only subscribe, and no data channel. The token lasts ten minutes; LiveKit refreshes it for a connected client.
- A connected client keeps its room until it leaves, so the match handler removes a player who leaves the table or is kicked through LiveKit's RoomService (`RemoveParticipant`, with a one-minute admin token). A seat that only lost its socket mid-round keeps voice while the seat is held.
- The server URL comes back in the token response, so moving to another box or to LiveKit Cloud is an environment change (`LIVEKIT_URL`, `LIVEKIT_API_KEY`, `LIVEKIT_API_SECRET`).
- All voice audio goes through Unity's audio: each remote voice plays from its own `AudioSource`, so the player's level, a level per player and ducking the table sounds are plain volumes (the SDK's platform audio has no volume control, so it is not used). A seat with its mic on sends Unity's microphone through the SDK's audio processing: echo cancellation against what Unity plays (table sounds included), noise suppression and gain control. The microphone opens at the first send, so a listener never sees the permission prompt; turning the mic on joins again with it. Holding mutes the published track. Muting a player, or turning the table's speaker off, unsubscribes, so that audio is not sent to this phone at all. iOS keeps the loudspeaker while recording (`Force IOS Speakers When Recording`).
- The cost rules of GDD §20.14 stay: join only while two mics are on, leave after two quiet minutes (OQ-044).
- Reliability: LiveKit's embedded TURN serves TCP and TLS on 443 for networks that block UDP; one node per region at first, more nodes behind Redis when one is full; LiveKit Cloud is the fallback if the box fails, by changing the three variables.

## Consequences
- Voice cost becomes part of the server bill: a few euros a month of extra traffic at launch instead of a per-minute fee.
- We run one more service: config, TLS for the TURN domain, the UDP port range in the firewall and its health in the runbooks.
- The Unity SDK adds a native library of about 8 MB compressed on Android arm64 and about 14 MB on iOS, close to Agora's voice SDK; it is a normal package dependency, so the voice code compiles in CI.
- Local development gets a LiveKit container in the compose file, so voice can be tested end to end on one Mac.
- Supersedes the voice rows in TECH_STACK §2, §4, §5 and §7 and ARCHITECTURE's voice mentions.

## Alternatives considered
See the table. Agora stays a possible adapter behind `IVoiceService` if self-hosting ever costs more than it saves.
