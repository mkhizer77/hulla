import { takeHand } from "./hand-take";
import { validateMove } from "./legal";
import { playCard } from "./trick";

import type { ApplyResult } from "../contract";
import type { ThullaEvent } from "./events";
import type { ThullaMove } from "./moves";
import type { ThullaState } from "./state";
import type { SeatIndex } from "@hulla/shared-ts";

/**
 * One validated move with no search and no draw afterwards: the result may carry a `pendingDraw`.
 * `applyMove` wraps this; the search calls it directly so a confirmation inside a branch never
 * triggers a nested search or playout.
 */
export function rawApply(
  state: ThullaState,
  seat: SeatIndex,
  move: ThullaMove,
): ApplyResult<ThullaState, ThullaEvent> {
  const error = validateMove(state, seat, move);
  if (error !== null) {
    return { ok: false, error };
  }
  return move.type === "take_hand" ? takeHand(state, seat) : playCard(state, seat, move.card);
}
