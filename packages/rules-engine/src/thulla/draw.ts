import { activeSeats, endRound, markEscaped, withSeat, type ThullaState } from "./state";

import type { ThullaEvent } from "./events";
import type { Rng, SeatIndex } from "@hulla/shared-ts";

/** Executes the pending draw for a chosen pool index; pure, so the search can enumerate it. */
export function executeDraw(state: ThullaState, index: number, events: ThullaEvent[]): ThullaState {
  const plan = state.pendingDraw;
  if (plan === null) {
    return state;
  }
  const card = plan.pool[index];
  if (card === undefined) {
    throw new Error("invariant: draw index inside the pool");
  }
  const leader = state.leaderSeat;
  events.push({
    type: "private_draw",
    seat: leader,
    card,
    source: plan.source,
    from: plan.from,
    visibleTo: plan.from === null ? [leader] : [leader, plan.from],
  });
  let seats = withSeat(state.seats, leader, (current) => ({ ...current, hand: [card] }));
  let discard = state.discard;
  if (plan.from === null) {
    discard = discard.filter((held) => held !== card);
  } else {
    seats = withSeat(seats, plan.from, (current) => ({
      ...current,
      hand: current.hand.filter((held) => held !== card),
    }));
  }
  let next: ThullaState = { ...state, seats, discard, pendingAutoLead: true, pendingDraw: null };
  if (plan.from !== null && next.seats[plan.from]?.hand.length === 0) {
    // E12: that was their last card; they escaped the moment their hand emptied.
    next = markEscaped(next, plan.from);
    events.push({ type: "escape", seat: plan.from, rank: next.escapeOrder.length });
    if (activeSeats(next).length === 1) {
      next = endRound(next, leader);
      events.push({ type: "round_ended", bhabhiSeat: leader, escapeOrder: next.escapeOrder });
    }
  }
  return next;
}

/** The real draw: one uniformly random card from the pool (R26, R28, R38). */
export function executeDrawWith(state: ThullaState, rng: Rng, events: ThullaEvent[]): ThullaState {
  if (state.pendingDraw === null) {
    return state;
  }
  return executeDraw(state, rng.nextInt(state.pendingDraw.pool.length), events);
}

export function hasPendingDraw(state: ThullaState): boolean {
  return state.pendingDraw !== null;
}

export type { SeatIndex };
