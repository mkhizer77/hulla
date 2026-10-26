# Urdu, Hindi and Bengali in UI Toolkit (#132)

Date: 26 October 2026. Unity 6000.4.9f1, UI Toolkit, Noto Nastaliq Urdu, Noto Sans Devanagari and Noto Sans Bengali (all OFL, in `Theme/Fonts`).

## What was tried
`ScriptShapingSnapshot` (play mode, explicit) renders the same page twice, once with the standard text generator and once with the advanced one: a sentence and a conjunct-heavy line per script, a button label, a right-to-left row, and a Latin font (Nunito) falling back to each script. It also writes line heights and the cost of changing thirty labels a frame. Run it with `-testFilter Hulla.Screens.Tests.ScriptShapingSnapshot` and `HULLA_SNAPSHOT_DIR`.

## Findings
- **The Advanced Text Generator was off.** It needs the project switch (`ProjectSettings/UIToolkitProjectSettings.asset`, `m_EnableAdvancedText`) and the style `-unity-text-generator: advanced`. Neither was set, so every screen used the standard generator. Both are now on; the style sits on `:root` in `Hulla.uss`.
- **Standard generator:** Urdu is unusable (letters don't join, most glyphs are missing, two lines break apart). Hindi and Bengali show broken conjuncts with visible halants (क्ष as क्‌ष), and Bengali puts its pre-base vowel signs after the consonant (খেলুন as খলেুন).
- **Advanced generator:** all three shape correctly: Nastaliq joins and stacks, Devanagari and Bengali conjuncts form, vowel signs reorder, Urdu digits and Latin mix in the same line. Fallback from Nunito to each script works through the font asset's fallback list.
- **Paragraph direction:** without `languageDirection = RTL`, an Urdu line ending in Latin or punctuation lays out left to right ("!" and "Rung" on the wrong end). The shell now sets it on the root for a right-to-left locale, next to the existing `rtl` class.
- **Line height:** at 44 px type a one-line Urdu label is 119 px tall, against about 65 px for one line of Latin, Hindi or Bengali. Nastaliq needs about 1.8 times the line height: rows that hold Urdu text need room to grow, not a fixed height.
- **Cost (Mac, M-series, editor):** first render with the three fonts loading took 604 ms (advanced) and 246 ms (standard). Changing thirty labels a frame cost 2.1 to 2.6 ms with the advanced generator and 4.4 to 5.0 ms with the standard one. Phone numbers are still to be measured.

## Not done here
- Screenshots on the low-tier device (#132 done-when) and the frame time there: they need the phone.
- Which fonts ship and how (in the binary, or as Addressables per locale) is for #100. The three fonts add about 1.8 MB.
- Setting the font per locale (Nastaliq for Urdu, Devanagari for Hindi) on the theme fonts, and the line-height rule for Urdu, are also for #100.

No blocker for the Unity version: 6000.4.9f1 handles all three scripts.
