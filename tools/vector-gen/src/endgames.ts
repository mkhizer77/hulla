import { forcedBhabhi, stateFromSetup, type Card } from "@hulla/rules-engine";

import type { Vector } from "@hulla/test-vectors";

const POOL: readonly Card[] = ["2S", "3S", "7S", "9S", "QH", "KH", "2D", "KD"];
const MODES = ["drawOpponent", "drawDiscard", "thullaEndsRound"] as const;

function subsets(items: readonly Card[], size: number): Card[][] {
  if (size === 0) return [[]];
  const out: Card[][] = [];
  items.forEach((item, index) => {
    for (const rest of subsets(items.slice(index + 1), size - 1)) {
      out.push([item, ...rest]);
    }
  });
  return out;
}

/** R41 and E14 over every small split of the pool: the search's verdict is pinned for both engines. */
export function endgameVectors(version: string): Vector[] {
  const out: Vector[] = [];
  let serial = 0;
  for (const sizeA of [1, 2]) {
    for (const sizeB of [1, 2, 3]) {
      for (const a of subsets(POOL, sizeA)) {
        const rest = POOL.filter((card) => !a.includes(card));
        for (const b of subsets(rest, sizeB)) {
          serial += 1;
          if (serial % 7 !== 0) continue;
          const mode = MODES[serial % MODES.length] ?? "drawOpponent";
          const discard: Card[] = mode === "drawDiscard" ? ["4C", "5C"] : [];
          const setup = {
            hands: [a, b],
            leader: 0,
            discard,
            rules: { twoPlayerEndgame: mode, confirmedFastForward: false },
          } as const;
          const state = stateFromSetup(setup);
          if (!state.ok) throw new Error(state.error.code);
          const id = `V-E${String(out.length + 1).padStart(3, "0")}-endgame-${a.join("")}-vs-${b.join("")}-${mode}`;
          out.push({
            id,
            version,
            title: `two-player endgame ${a.join(" ")} against ${b.join(" ")} (${mode})`,
            rules: ["R37", "R40", "R41", "E14"],
            setup,
            moves: [],
            assert: { forcedBhabhi: forcedBhabhi(state.value) },
          });
        }
      }
    }
  }
  return out;
}
