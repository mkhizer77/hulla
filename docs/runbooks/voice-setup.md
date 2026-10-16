# Voice setup

Table voice (GDD 11.4, #32) runs on Agora behind `IVoiceService` (ADR-0012). The client code is in the repository; the Agora project, the SDK import and the define are the owner's.

## How it behaves
- Each seat has a mic button beside Chat. Its state goes to the server (`set_mic`) and back to everyone in `table_state`.
- A client joins the table's Agora channel only while two mics at the table are on (its own counted), and leaves after two minutes in which nobody spoke. After that, the next mic change brings voice back (GDD 20.14, OQ-044). Spectators listen only.
- Private rooms default to open mic; public and ranked tables to push-to-talk, where a Hold to talk button appears once the channel is open. A preset's `voiceDefault` overrides both; `off` hides voice.
- Muting a player in the player sheet silences their voice on this device too. The mute list is on the server, so it holds after a reconnect.
- A failed or dropped channel never blocks play (E25): the pill says so and the mic button retries.
- The server's `voice` switch in remote config hides voice everywhere.

## Agora project
1. Create a project in the Agora console with **App ID + Token** (secured mode). Turn on the App Certificate.
2. Put both in the Nakama runtime env, from the box's secrets file, then restart Nakama:

| Variable | What it is |
|---|---|
| `AGORA_APP_ID` | The project's App ID (32 hex characters). Public by design; the client gets it from `voice_token`. |
| `AGORA_APP_CERTIFICATE` | The primary certificate. Secret; it never leaves the server. |

Without them `voice_token` answers `voice.unavailable` and the table shows that voice didn't connect.

## Unity client
1. Download the Agora Voice SDK for Unity (4.x) from Agora's download page and import it into `apps/unity/Hulla` (audio only is enough).
2. If the SDK folder has no assembly definition, add one at its root named `Agora.Rtc` (Unity can't reference loose scripts from an asmdef).
3. Add `Agora.Rtc` to the references of `Assets/_Project/Infrastructure/Hulla.Infrastructure.asmdef`, and to the allowed list for `Hulla.Infrastructure` in `AssemblyGraphTests`.
4. Add the scripting define `HULLA_AGORA` for Android and iOS (Project Settings, Player, Other Settings). `AgoraVoiceService` compiles only with it.
5. iOS: set the microphone usage description (Player, Other Settings). Android: the SDK's manifest adds `RECORD_AUDIO`.

Without the define, the editor uses `FakeVoiceService` (joins succeed, no audio, your own ring lights while the mic is on) and device builds hide voice. Pass `-hulla-fake-voice` to a Mac or Android build to use the fake there.

## Checking it
1. Two phones and a third device at a private table on the phase 0 box, with the variables set and a build made with `HULLA_AGORA`.
2. One mic on: the pill says voice starts when another mic is on. A second mic on: both join and hear each other; rings light on the speaker's seat.
3. A public table: Hold to talk sends; letting go stops.
4. Mute a talking player from their badge: they go silent here. Turn the phone's network off and on: after the rejoin they are still silent.
5. Five players through a full round (#32 done-when).
6. Leave everyone quiet for two minutes: the pill says voice paused; turning a mic on brings it back.

## Not done yet
Per-seat volume, ducking the table sounds under speech, keeping voice for two minutes with the screen locked (VoIP background mode), Bluetooth and earpiece choice in settings, and the under-16 voice limits (needs an age field at sign-up).
