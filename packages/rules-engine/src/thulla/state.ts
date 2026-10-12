import { THULLA_RULES_VERSION, type ThullaRules } from "./rules";

import type { Card, Suit } from "../card-kit";
import type { SeatSpec } from "../contract";
import type { SeatIndex } from "@hulla/shared-ts";

export { THULLA_RULES_VERSION };

/** 3 to 6 seats in v1.0 (GDD R3); 7 and 8 arrive with two decks in v1.2. */
export const THULLA_SEATS: SeatSpec = { min: 3, max: 6 };

export type ThullaPhase = "opening" | "play" | "ended";

/*
 * Everything is readonly; moves return new objects (ARCHITECTURE 2.3). Absent values are `null`,
 * never `undefined`, so canonical JSON is stable; `stateHash` accepts these interfaces directly.
 */

export interface SeatState {
  /** Always in canonical order, so the hash never depends on arrival order (ARCHITECTURE 7.5). */
  readonly hand: readonly Card[];
  readonly escaped: boolean;
  /** 1 for the first escape, and so on; null while active (R11, R25). */
  readonly escapeRank: number | null;
}

/** Per-seat counters for the results screen and stats (R49). */
export interface SeatStats {
  readonly thullasGiven: number;
  readonly thullasReceived: number;
  readonly cardsPickedUp: number;
  readonly handsTaken: number;
}

export const EMPTY_STATS: SeatStats = {
  thullasGiven: 0,
  thullasReceived: 0,
  cardsPickedUp: 0,
  handsTaken: 0,
};

/**
 * A draw the resolution decided on but has not executed yet, so the forced-outcome search can
 * enumerate every card in `pool` (R41). `applyMove` always executes it before returning.
 */
export interface PendingDraw {
  readonly source: "discard" | "nextPlayer" | "opponent";
  readonly from: SeatIndex | null;
  readonly pool: readonly Card[];
}

export interface TrickState {
  readonly ledSuit: Suit | null;
  readonly plays: readonly { readonly seat: SeatIndex; readonly card: Card }[];
  readonly thullaBy: SeatIndex | null;
}

export interface ThullaState {
  readonly rulesVersion: string;
  readonly rules: ThullaRules;
  readonly seatCount: number;
  readonly dealerSeat: SeatIndex;
  readonly leaderSeat: SeatIndex;
  /** The seat expected to act, or null while every pending seat may act at once (R13). */
  readonly turnSeat: SeatIndex | null;
  readonly phase: ThullaPhase;
  readonly trickIndex: number;
  readonly seats: readonly SeatState[];
  readonly trick: TrickState;
  /** Face-down pile in insertion order; the R26 draw picks an index uniformly (server-only). */
  readonly discard: readonly Card[];
  /** Seats still to respond in the simultaneous opening trick (R13). */
  readonly openingPending: readonly SeatIndex[];
  /** One take per pre-lead window (R35); reset every trick. */
  readonly handTakeUsed: boolean;
  /** The leader just drew a card and must lead it; nothing else is legal (R26, R38). */
  readonly pendingAutoLead: boolean;
  readonly stats: readonly SeatStats[];
  readonly escapeOrder: readonly SeatIndex[];
  readonly bhabhiSeat: SeatIndex | null;
  /** R41: set once the forced-outcome search proves the Bhabhi; the search never runs again. */
  readonly confirmedBhabhi: SeatIndex | null;
  readonly pendingDraw: PendingDraw | null;
  /** Public before the deal: SHA-256(seed ‖ canonicalJson(deckOrder)) (R6). */
  readonly commitment: string;
  /** Server-only until the round ends, then revealed so clients verify the commitment (R6). */
  readonly seedHex: string;
  readonly deckOrder: readonly Card[];
}

export const EMPTY_TRICK: TrickState = { ledSuit: null, plays: [], thullaBy: null };

export function withSeat(
  seats: readonly SeatState[],
  seat: SeatIndex,
  update: (current: SeatState) => SeatState,
): SeatState[] {
  return seats.map((current, index) => (index === seat ? update(current) : current));
}

export function withStats(
  stats: readonly SeatStats[],
  seat: SeatIndex,
  update: (current: SeatStats) => SeatStats,
): SeatStats[] {
  return stats.map((current, index) => (index === seat ? update(current) : current));
}

/** R25: the seat leaves the turn order with the next escape rank. */
export function markEscaped(state: ThullaState, seat: SeatIndex): ThullaState {
  const escapeRank = state.escapeOrder.length + 1;
  return {
    ...state,
    seats: withSeat(state.seats, seat, (current) => ({ ...current, escaped: true, escapeRank })),
    escapeOrder: [...state.escapeOrder, seat],
  };
}

/** R10: the last active seat is the Bhabhi; the round is over. */
export function endRound(state: ThullaState, bhabhiSeat: SeatIndex): ThullaState {
  return {
    ...state,
    phase: "ended",
    bhabhiSeat,
    turnSeat: null,
    trick: EMPTY_TRICK,
    pendingAutoLead: false,
    pendingDraw: null,
    openingPending: [],
  };
}

export function activeSeats(state: ThullaState): SeatIndex[] {
  const seats: SeatIndex[] = [];
  state.seats.forEach((seat, index) => {
    if (!seat.escaped) {
      seats.push(index);
    }
  });
  return seats;
}

/** The next seat clockwise from `from` that is still active; `from` itself if it is the only one. */
export function nextActiveSeat(state: ThullaState, from: SeatIndex): SeatIndex {
  for (let step = 1; step <= state.seatCount; step++) {
    const seat = (from + step) % state.seatCount;
    if (!state.seats[seat]?.escaped) {
      return seat;
    }
  }
  return from;
}
