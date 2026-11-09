# Voice at the table: how it should work

Review of voice chat after the first test on the phase 0 box (9 November 2026). GDD §11.4, §20.14, OQ-044, ADR-0024.

## What went wrong in the test

Nobody heard anybody. Four causes, in order of weight:

1. **The Mac app never asked for the microphone.** The permission check only ran on phones, so a Mac window showed "Mic on" while macOS refused the microphone. It sent nothing, and logged a warning on every state change.
2. **A mic that failed to open still said "Mic on".** The player had no way to know they were not heard.
3. **Voice opened only once two mics were on.** If one player turned their mic on, nobody joined the room, even friends with voices on. That rule existed to save Agora minutes. LiveKit runs on our own box, so a quiet room costs next to nothing.
4. **Mac windows in the background are paused** (Unity's run-in-background is off). On the Mac test rig, only the front window plays sound or sends voice. Test voice with phones, or with one Mac window and phones.

Causes 1 to 3 are fixed in this change. Table sounds were never the problem: the audio meter below shows card sounds playing on the Pixel and the Mac at normal level (peaks around 0.35). After the fix, speech picked up by the phone's mic played on the Mac with peaks of 0.93.

## The flow we want

### Private room (open mic)

1. Every seat sees the mic button, off, next to chat. Voices are on by default.
2. The first tap shows one line ("Your mic is used only at this table, and only while it's on.") followed by the system prompt.
3. Once allowed, the mic is live straight away whenever anyone else is at the table. Nobody else needs their mic on to hear you.
4. Your ring glows while you speak, and so does each speaker's ring.
5. Tapping the mic again turns it off.
6. The speaker chip turns voices off on this phone. With the mic off too, the phone leaves the room.
7. Tapping a player opens their sheet, with their voice level and Mute.

### Public tables and Quick Play (push-to-talk)

Hold the mic to talk and release to stop. The first hold asks for permission.

### When something fails

- **Mic refused or withdrawn:** the mic turns off and the pill says "Mic not allowed · turn on Hulla's microphone in Settings to talk". You keep hearing the others.
- **Voice server unreachable:** the pill says play goes on, and tapping the mic retries (E25).

## Issues and suggestions

| # | Issue | Suggestion | Status |
|---|---|---|---|
| 1 | Mac never asked for the microphone | Ask like iOS does | Done |
| 2 | A failed mic still read "Mic on" | Turn it off and say how to allow it | Done |
| 3 | One mic alone was never heard | Open the room when any mic is on and someone else is there | Done |
| 4 | Push-to-talk needs two gestures: a tap to arm the mic, then a hold to talk. A first-timer taps, sees "Mic on", and isn't heard. | On push-to-talk tables the button only does hold-to-talk; the first hold asks for permission | Proposed |
| 5 | "Mic on" / "Mic off" as button text reads as both a state and an action | An icon-only mic: slashed when off, accent fill when live, a level ring while you speak; the screen reader says "Microphone, on" | Proposed |
| 6 | Nothing confirms you are heard | Show your input level on the button, and "Nobody is listening" when every other player has voices off | Proposed |
| 7 | After two quiet minutes voice pauses and needs a tap ("Voice paused after a quiet spell"). Card tables often go quiet for that long. | Drop the silence leave for seated players and leave only when the app goes to the background. Self-hosted voice makes this cheap (OQ-044). | Proposed |
| 8 | The voices chip only shows while voice is live, so you can't silence voices before they start | Keep a speaker button next to the mic whenever voice exists | Proposed |
| 9 | When someone turns their mic on, nothing invites the others to join in | Show a one-time pill: "Ali turned their mic on · tap the mic to talk" | Proposed |
| 10 | After "don't ask again" on Android, tapping the mic does nothing | When the permission is blocked, tapping the mic opens the app's settings page | Proposed |
| 11 | No way to check the mic before a round | A short mic test in Settings → Voice that plays back two seconds of your voice | Proposed |
| 12 | The lobby has a mic button but no voice pill, no speaking rings and no first-tap explanation, so it isn't clear whether voice is on before the round starts. The lobby is when friends talk most. | Give the lobby the table's voice row: the pill, rings on the seat ring, and the speaker chip | Proposed |

## Checking audio on a device

Development builds log when the app's own output starts and stops sounding ("audio: sounding", "audio: quiet"), for table sounds and voices alike. Read these lines with `adb logcat -s Unity` on Android, or in `~/Library/Logs/Hulla/Hulla/Player.log` on the Mac. If they appear and you still hear nothing, the cause is outside the app: the phone's media volume, Do Not Disturb, or a Bluetooth headset.
