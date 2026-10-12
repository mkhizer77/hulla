import { stateFromSetup, type ThullaSetup } from "../setup";

import type { Card } from "../../card-kit";
import type { ThullaEvent } from "../events";
import type { ThullaState } from "../state";
import type { Rng, SeatIndex } from "@hulla/shared-ts";

export type Setup = ThullaSetup;

/** Builds a full explicit state the way the shared vectors do; throws on a bad setup in tests. */
export function stateFrom(setup: Setup): ThullaState {
  const state = stateFromSetup(setup);
  if (!state.ok) {
    throw new Error(`bad test setup: ${state.error.code} ${JSON.stringify(state.error.detail)}`);
  }
  return state.value;
}

/** An Rng that answers `nextInt` with the given indices in order (mod the bound), then zeros. */
export function fixedRng(indices: readonly number[] = []): Rng {
  let cursor = 0;
  return {
    nextUint32: () => 0,
    nextInt: (n) => {
      const value = indices[cursor] ?? 0;
      cursor += 1;
      return value % n;
    },
  };
}

export function types(events: readonly ThullaEvent[]): string[] {
  return events.map((event) => event.type);
}

export function handOf(state: ThullaState, seat: SeatIndex): readonly Card[] {
  return state.seats[seat]?.hand ?? [];
}

export function cardsAtTable(state: ThullaState): number {
  const inHands = state.seats.reduce((sum, seat) => sum + seat.hand.length, 0);
  return inHands + state.discard.length + state.trick.plays.length;
}
