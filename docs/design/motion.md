# Motion and haptics

The motion sheet for the app (GDD 15.7, 27, #81). The source of truth is `Presentation/Theme/MotionSheet.cs`; `MotionSheetTests` fails when a stylesheet animates something the sheet does not list, uses another duration or curve, or has no reduce-motion variant.

## Reduce motion

The setting puts `motion--reduced` on the shell root. Nothing travels, grows or pops: a state change snaps or fades. Card and trick flights, the screen entry and the thulla stamp pick their fade in code; the stylesheet's own transitions have rules under `.motion--reduced` at the end of `Hulla.uss`. The shake and the ambient scene stop, and the timer ring stops pulsing.

## Transitions

| What | Duration | Curve | Reduce motion |
|---|---|---|---|
| Screen enters, rising 24 px | 240 ms | ease-out cubic | 120 ms fade |
| Button and chip press, dip to 0.97 | 80 ms | ease | kept (the player's own press) |
| Tap to raise a card in the hand | 120 ms | ease | snaps up |
| Card played to the trick | 340 ms | ease-out cubic | 200 ms fade in |
| Trick to the picker or the discard, 70 ms apart | 460 ms | ease-in-out cubic | 300 ms fade |
| My picked-up cards glide into the hand | 460 ms | ease-in-out cubic | appear in place |
| Discard count bumps to 1.3 | 120 ms | ease | no bump |
| Seat on turn grows to 1.08 under the gold ring | 220 ms | ease-out back | snaps |
| Lobby seat face pops in from 0.6 | 220 ms | ease-out back | full size at once |
| Daily coins rise 120 px and fade | 1.3 s | ease-out, ease-in | fade in place |
| Thulla stamp | 120 ms in, 900 ms hold, 200 ms out, 1.08 overshoot | `StampTimeline` | fade, no shake |
| Timer ring | linear with the clock | — | no pulse at 5 s |

The card flights run longer than GDD 15.7's first numbers (220 ms to the trick, 400 ms plus 40 ms to the picker): they were lengthened on 14 October (PR 260) so a played card can be followed by eye on a phone. A seat avatar has two transition rules in `Hulla.uss` (160 ms and 220 ms); the later one, 220 ms ease-out back, is the one that applies, and both are on the sheet.

## Haptics

Off with the Haptics setting. Android plays the patterns through the vibrator with amplitude; iOS plays UIKit's feedback through `Plugins/iOS/HullaHaptics.mm`.

| Event | Android | iOS |
|---|---|---|
| Turn start | 18 ms at 90 | light impact |
| Card played | 10 ms at 60 | soft impact |
| You gave a thulla | two 30 ms beats at 160 | medium impact |
| You picked up | 60 ms at 255 | heavy impact |
| You escaped | two 20 ms beats at 120 | success |
| You are the Bhabhi | 220 ms at 140 | error |
