import type { Card, Suit } from "../card-kit";
import type { SeatSpec } from "../contract";
import type { ThullaRules } from "./rules";
import type { SeatIndex } from "@hulla/shared-ts";

/** 3 to 6 seats in v1.0 (GDD R3); 7 and 8 arrive with two decks in v1.2. */
export const THULLA_SEATS: SeatSpec = { min: 3, max: 6 };

export type ThullaPhase = "opening" | "play" | "ended";

/*
 * State types are aliases, not interfaces, so the whole state is a `JsonValue` and `stateHash`
 * can take it directly. Everything is readonly; moves return new objects (ARCHITECTURE 2.3).
 * Absent values are `null`, never `undefined`, so canonical JSON is stable.
 */

export interface SeatState {
  /** Always in canonical order, so the hash never depends on arrival order (ARCHITECTURE 7.5). */
  readonly hand: readonly Card[];
  readonly escaped: boolean;
  /** 1 for the first escape, and so on; null while active (R11, R25). */
  readonly escapeRank: number | null;
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
  readonly escapeOrder: readonly SeatIndex[];
  readonly bhabhiSeat: SeatIndex | null;
  /** Public before the deal: SHA-256(seed ‖ canonicalJson(deckOrder)) (R6). */
  readonly commitment: string;
  /** Server-only until the round ends, then revealed so clients verify the commitment (R6). */
  readonly seedHex: string;
  readonly deckOrder: readonly Card[];
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
