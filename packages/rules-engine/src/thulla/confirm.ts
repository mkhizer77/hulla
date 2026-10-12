import { canonicalJson, type SeatIndex } from "@hulla/shared-ts";

import { sortCards } from "../card-kit";

import { rawApply } from "./apply";
import { executeDraw } from "./draw";
import { legalMoves } from "./legal";
import { activeSeats, type ThullaState } from "./state";

/** ARCHITECTURE 7.5: the search is capped; hitting the cap means "not confirmed". */
export const CONFIRM_NODE_CAP = 50_000;

/** R41 runs after a resolution in the endgame with twelve or fewer cards in play, before any draw. */
export function shouldSearch(state: ThullaState): boolean {
  return (
    state.phase === "play" &&
    state.confirmedBhabhi === null &&
    state.trick.plays.length === 0 &&
    activeSeats(state).length === 2 &&
    cardsInPlay(state) <= 12
  );
}

export function cardsInPlay(state: ThullaState): number {
  return state.seats.reduce((sum, seat) => sum + seat.hand.length, 0);
}

type Outcome = SeatIndex | "mixed";

/**
 * R41, E14: evaluates every legal choice by both players and every possible random draw. Returns
 * the Bhabhi when every continuation agrees, null when they differ or the node cap is reached.
 */
export function forcedBhabhi(
  state: ThullaState,
  maxNodes: number = CONFIRM_NODE_CAP,
): SeatIndex | null {
  const memo = new Map<string, Outcome>();
  let nodes = 0;

  function visit(current: ThullaState): Outcome | undefined {
    if (current.phase === "ended") {
      return current.bhabhiSeat ?? "mixed";
    }
    const key = keyOf(current);
    const known = memo.get(key);
    if (known !== undefined) {
      return known;
    }
    nodes += 1;
    if (nodes > maxNodes) {
      return undefined;
    }
    let outcome: Outcome | null = null;
    for (const child of children(current)) {
      const result = visit(child);
      if (result === undefined) {
        return undefined;
      }
      outcome = outcome === null || outcome === result ? result : "mixed";
      if (outcome === "mixed") {
        break;
      }
    }
    const settled: Outcome = outcome ?? "mixed";
    memo.set(key, settled);
    return settled;
  }

  const result = visit(state);
  return result === undefined || result === "mixed" ? null : result;
}

/** A chance node enumerates every card of the pool; a decision node every legal move of every seat. */
function children(state: ThullaState): ThullaState[] {
  if (state.pendingDraw !== null) {
    return state.pendingDraw.pool.map((_, index) => executeDraw(state, index, []));
  }
  const out: ThullaState[] = [];
  for (const seat of activeSeats(state)) {
    for (const move of legalMoves(state, seat)) {
      const result = rawApply(state, seat, move);
      if (result.ok) {
        out.push(result.state);
      }
    }
  }
  return out;
}

function keyOf(state: ThullaState): string {
  return canonicalJson({
    hands: state.seats.map((seat) => seat.hand),
    escaped: state.seats.map((seat) => seat.escaped),
    leader: state.leaderSeat,
    turn: state.turnSeat,
    plays: state.trick.plays,
    auto: state.pendingAutoLead,
    draw: state.pendingDraw,
    discard: state.rules.twoPlayerEndgame === "drawDiscard" ? sortCards(state.discard) : null,
  });
}
