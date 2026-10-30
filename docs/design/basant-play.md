# Basant: Quick Play, the table and the results

Built 28 Oct 2026 to the approved concepts of 27 Oct.

- **Quick Play** is a 1080 x 1936 stage (`QuickPlayView`): a dashed seat for every player the table is for, "Finding a table for N", the seconds so far against the usual 20, the seat-size medallions with the chosen one lit, the search note, and the cross to cancel.
- **The table** keeps its responsive frame (the seat ring, the trick pile, the fanned hand, the animations); the kit dresses it: the calm sky, the rug under the seats, cream name plates and status tags, the count badge, the crown on the host, the gold halo on the active seat, the dark turn pill, pill chips for quick chat, round icon buttons for the mic and chat, the red card back.
- **The results** sit on the felt: the three places stand on the drawn podium steps, the Bhabhi beside them under the stamp, the reward line, highlights on the label plate, Play again on the yellow button, Change rules and the share on pills.

## Loading

The first screen, up from boot until Home is ready (`LoadingView`, `AppFlow.Start`). The HULLA kite sits on the Home sky, which already flies its own kites, with the kit's bar under it and one word. The bar fills in four steps: content opened, chat lists, presets, then the quiet sign-in of a returning player, which gets 2.5 s so Home opens with the balance and friends; a slow network does not hold the door. The screen stays at least 0.6 s so the kite does not flash. The first-launch gate and the consent question come after it, on Home, as before.
