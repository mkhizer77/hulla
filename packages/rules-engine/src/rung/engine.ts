import { completeHands, redealRung } from "./deal";
import { validateRungMove } from "./legal";
import { teamOf, type RungState } from "./state";
import { endRungRound, playRungCard, prizeAtSeven } from "./trick";

import type { ApplyResult } from "../contract";
import type { RungEvent } from "./events";
import type { RungMove } from "./moves";
import type { Rng, SeatIndex } from "@hulla/shared-ts";

export { canRedeal, currentRungPhase, isRungRoundOver, legalRungMoves } from "./legal";

/**
 * Total: every failure is a catalogue error, never a throw. The only randomness is a redeal's
 * shuffle (RG7), drawn from the injected `Rng`.
 */
export function applyRungMove(
  state: RungState,
  seat: SeatIndex,
  move: RungMove,
  rng: Rng,
): ApplyResult<RungState, RungEvent> {
  const error = validateRungMove(state, seat, move);
  if (error !== null) {
    return { ok: false, error };
  }
  switch (move.type) {
    case "redeal": {
      const next = redealRung(state, rng);
      return {
        ok: true,
        state: next,
        events: [{ type: "redealt", commitment: next.commitment, redeals: next.redeals }],
      };
    }
    case "call_trump": {
      // RG6, RG8 and RG10: the rung is named, the hands are completed, the caller leads.
      const hands = completeHands(state.hands, state.stock, state.callerSeat);
      const next: RungState = {
        ...state,
        trump: move.suit,
        hands,
        stock: [],
        phase: "play",
        turnSeat: state.callerSeat,
      };
      return {
        ok: true,
        state: next,
        events: [
          { type: "trump_called", seat, suit: move.suit },
          { type: "hands_completed", handCounts: hands.map((hand) => hand.length) },
        ],
      };
    }
    case "court_choice": {
      const events: RungEvent[] = [{ type: "court_choice_made", seat, playOn: move.playOn }];
      const team = teamOf(seat);
      if (!move.playOn) {
        return { ok: true, state: endRungRound(state, team, prizeAtSeven(state), events), events };
      }
      // RG26: the chase is on; the chooser won the last trick and leads.
      return { ok: true, state: { ...state, phase: "play", chasing: team }, events };
    }
    case "play_card": {
      const played = playRungCard(state, seat, move.card);
      return { ok: true, state: played.state, events: played.events };
    }
  }
}
