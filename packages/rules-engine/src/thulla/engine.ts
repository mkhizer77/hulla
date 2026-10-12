import { rawApply } from "./apply";
import { autoPlayMove } from "./autoplay";
import { cardsInPlay, forcedBhabhi, shouldSearch } from "./confirm";
import { executeDrawWith } from "./draw";

import type { ApplyResult } from "../contract";
import type { ThullaEvent } from "./events";
import type { ThullaMove } from "./moves";
import type { ThullaState } from "./state";
import type { Rng, SeatIndex } from "@hulla/shared-ts";

export { canTakeHand, currentPhase, isRoundOver, legalMoves } from "./legal";

/** R40 bounds every round; this only guards the fast-forward loop against a broken invariant. */
const FAST_FORWARD_CAP = 1000;

/**
 * Total: every failure is a catalogue error, never a throw (R47). After a resolution in the
 * endgame the forced-outcome search runs before any pending draw is realised (R41); the draw is
 * then executed with the `Rng`, and a confirmed round is played out at once with the R44 policy
 * when `confirmedFastForward` is on, so the same call ends the round.
 */
export function applyMove(
  state: ThullaState,
  seat: SeatIndex,
  move: ThullaMove,
  rng: Rng,
): ApplyResult<ThullaState, ThullaEvent> {
  const applied = rawApply(state, seat, move);
  if (!applied.ok) {
    return applied;
  }
  let next = applied.state;
  const events = [...applied.events];
  if (shouldSearch(next)) {
    const bhabhiSeat = forcedBhabhi(next);
    if (bhabhiSeat !== null) {
      next = { ...next, confirmedBhabhi: bhabhiSeat };
      events.push({
        type: "confirmed",
        bhabhiSeat,
        cardsRemaining: cardsInPlay(next),
        fastForward: next.rules.confirmedFastForward,
      });
    }
  }
  next = executeDrawWith(next, rng, events);
  if (next.confirmedBhabhi !== null && next.rules.confirmedFastForward && next.phase !== "ended") {
    return playOut(next, rng, events);
  }
  return { ok: true, state: next, events };
}

/** R41: the remaining plays resolve with the R44 policy; the outcome is forced, so it cannot change. */
function playOut(
  start: ThullaState,
  rng: Rng,
  events: ThullaEvent[],
): ApplyResult<ThullaState, ThullaEvent> {
  let state = start;
  for (let step = 0; step < FAST_FORWARD_CAP && state.phase !== "ended"; step++) {
    const seat = state.turnSeat ?? state.leaderSeat;
    const move = autoPlayMove(state, seat);
    if (move === null) {
      break;
    }
    const result = rawApply(state, seat, move);
    if (!result.ok) {
      break;
    }
    events.push(...result.events);
    state = executeDrawWith(result.state, rng, events);
  }
  return { ok: true, state, events };
}
