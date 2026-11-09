# Accessibility audit

The pass B audit against GDD 27 (#83), November 2026. Each row says what the app does, what proves it in the repository, and whether it still needs a check on a phone. The beta gate's baseline (GDD 28.5) is this sheet with every "In the code" row done and the phone column ticked in the device session.

| Need | What the app does | Proof in the repository | In the code | On a phone |
|---|---|---|---|---|
| Colour vision | Four-colour deck; high-contrast theme; state is never colour only (the turn has the ring and the timer, a void has a chip, Out has a word) | `ThemeContrastTests`, `AccessibilityScreenTests` (contrast on every screen) | Done | Check the four-colour deck at the table |
| Text size | Text at 125, 150 and 200%; large cards | `AccessibilityScreenTests`, `PseudoLocaleTests` (longer text fits) | Done | Read Home and the table at 200% |
| Screen reader | TalkBack and VoiceOver read every screen through Unity's accessibility hierarchy: buttons, toggles, fields, headings and text in reading order; a double tap runs the button; cards in the hand read by name ("King of Hearts", or "Two of Clubs, can't be played now") and a double tap plays one | `ScreenReaderTests` (every visible button on every screen has words, the hand reads its cards, a double tap runs the action) | Done | A round from Home to results with TalkBack and with VoiceOver |
| Spoken play | The table announces each card played ("Sara played King of Hearts"), a thulla, a pick-up with its count, escapes, your turn and the Bhabhi | `ScreenReaderTests.CardsAndMomentsAreSpokenInWords` | Done | Hear a full round |
| Motion | Reduce motion: no flights, pops or shake; fades instead; the ambient scene and the timer pulse stop | `MotionSheetTests`, `TimerPhaseTests`, `docs/design/motion.md` | Done | Play a round with it on |
| Flashing | Nothing pulses faster than 2.5 times a second (the red timer); nothing flashes | `TimerPhaseTests.NothingPulsesFasterThanThreeTimesASecond` | Done | — |
| Hearing | Every sound has a visual: the stamp, the ring, the badges, speaking rings on seats; no information is in sound alone | `VoiceControllerTests` (speaking state), `StampTimelineTests`, `TimerPhaseTests` | Done | — |
| Subtitles | Subtitles for voice-over lines | — | Waits for the first voice-over pack (#89); no voice-over ships yet | — |
| Tap targets | Controls at least 48 dp; the table's top-bar and quick-chat pills are 26 dp (above WCAG 2.2's 24 dp), the owner's call in OQ-043 | `AccessibilityScreenTests` | Done | — |
| One-tap play | A setting plays a legal card on the first tap | `SettingsModelTests` (the setting); `HandView` plays on the first tap when it is on | Done | Play a round with it on |
| Left-handed | The hand and the controls mirror (`hand--left` on the shell root) | `SettingsModelTests` (the setting); no layout test yet | Done | Check the table mirrored |
| Time | A Relaxed preset gives a minute a turn in private rooms; the host can also pick 60 s in the rules | `table_rpcs.test.ts` (presets), content `presets/thulla/relaxed.json` | Done | — |
| Cognitive | The rulebook at the table, legal-card highlights with a one-line reason, tutorial coaching and first-round hints | `PlayHintTests`, `ScreenSmokeTests` | Done | — |
| Voice alternatives | Quick chat and emotes carry the table for players who don't speak | `QuickStripTests` | Done | — |
| Errors | Plain words with Retry, never a raw code | `ListStatesTests`, `docs/design/list-states.md` | Done | — |

## How the screen reader works

`Presentation/Accessibility`: `ScreenReaderTree` reads the visible panel in order; `ScreenReader` publishes it as Unity's `AccessibilityHierarchy` while the phone's screen reader is on, rebuilding only when what it would say changes, and sends announcements; `Accessible` names art-only elements such as cards; `Speech` holds the spoken words, from `content/strings` (`card.*`, `speech.*`). Frames are in screen pixels with the origin at the top left. Check that on both phones: if VoiceOver's focus box sits upside down, the frames need the y axis flipped.

## Left for the device session

- A round from Home to results with TalkBack on the Pixel 7a and VoiceOver on the iPhone, using only the screen reader.
- The rows above marked for a phone.
