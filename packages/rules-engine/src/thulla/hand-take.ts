import { sortCards } from "../card-kit";

import {
  activeSeats,
  markEscaped,
  nextActiveSeat,
  withSeat,
  withStats,
  type ThullaState,
} from "./state";

import type { ApplyResult } from "../contract";
import type { ThullaEvent } from "./events";
import type { SeatIndex } from "@hulla/shared-ts";

/**
 * R33: every card of the nearest clockwise active player moves to the taker, the target escapes
 * with the next rank, the table hears both names and the count, and the taker leads.
 */
export function takeHand(
  state: ThullaState,
  seat: SeatIndex,
): ApplyResult<ThullaState, ThullaEvent> {
  const target = nextActiveSeat(state, seat);
  const cards = state.seats[target]?.hand ?? [];
  const seats = withSeat(
    withSeat(state.seats, seat, (taker) => ({
      ...taker,
      hand: sortCards([...taker.hand, ...cards]),
    })),
    target,
    (current) => ({ ...current, hand: [] }),
  );
  const stats = withStats(state.stats, seat, (current) => ({
    ...current,
    handsTaken: current.handsTaken + 1,
  }));
  const events: ThullaEvent[] = [
    { type: "hand_taken", taker: seat, target, cardCount: cards.length },
  ];
  const taken = markEscaped(
    { ...state, seats, stats, leaderSeat: seat, turnSeat: seat, handTakeUsed: true },
    target,
  );
  events.push({ type: "escape", seat: target, rank: taken.escapeOrder.length });
  const active = activeSeats(taken);
  if (active.length === 2) {
    events.push({ type: "endgame_started", seats: active });
  }
  return { ok: true, state: taken, events };
}
