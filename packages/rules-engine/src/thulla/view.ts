import { canTakeHand } from "./legal";

import type { Card, Suit } from "../card-kit";
import type { Viewer } from "../contract";
import type { ThullaEvent } from "./events";
import type { ThullaRules } from "./rules";
import type { SeatStats, ThullaPhase, ThullaState } from "./state";
import type { SeatIndex } from "@hulla/shared-ts";

export interface SeatView {
  readonly handCount: number;
  /** The viewer's own cards; null for every other seat and for spectators. */
  readonly hand: readonly Card[] | null;
  readonly escaped: boolean;
  readonly escapeRank: number | null;
}

/** What a seat or spectator may know (GDD 4.1 rule 2, R23, R6, ARCHITECTURE 1). */
export interface ThullaView {
  readonly viewer: Viewer;
  readonly rulesVersion: string;
  readonly rules: ThullaRules;
  readonly seatCount: number;
  readonly dealerSeat: SeatIndex;
  readonly leaderSeat: SeatIndex;
  readonly turnSeat: SeatIndex | null;
  readonly phase: ThullaPhase;
  readonly trickIndex: number;
  readonly seats: readonly SeatView[];
  readonly trick: {
    readonly ledSuit: Suit | null;
    readonly plays: readonly { readonly seat: SeatIndex; readonly card: Card }[];
    readonly thullaBy: SeatIndex | null;
  };
  readonly discardCount: number;
  readonly openingPending: readonly SeatIndex[];
  readonly handTakeUsed: boolean;
  readonly pendingAutoLead: boolean;
  readonly stats: readonly SeatStats[];
  readonly escapeOrder: readonly SeatIndex[];
  readonly bhabhiSeat: SeatIndex | null;
  readonly confirmedBhabhi: SeatIndex | null;
  readonly commitment: string;
  /** Revealed when the round has ended so anyone can verify the commitment (R6). */
  readonly seedHex: string | null;
  readonly deckOrder: readonly Card[] | null;
  /** Whether the viewer may take a hand right now (null for spectators). */
  readonly canTakeHand: boolean | null;
}

/** Total over every state and viewer; hidden information never leaves here (ARCHITECTURE 7.1). */
export function redact(state: ThullaState, viewer: Viewer): ThullaView {
  const ownSeat = viewer.kind === "seat" ? viewer.seat : null;
  const ended = state.phase === "ended";
  return {
    viewer,
    rulesVersion: state.rulesVersion,
    rules: state.rules,
    seatCount: state.seatCount,
    dealerSeat: state.dealerSeat,
    leaderSeat: state.leaderSeat,
    turnSeat: state.turnSeat,
    phase: state.phase,
    trickIndex: state.trickIndex,
    seats: state.seats.map((seat, index) => ({
      handCount: seat.hand.length,
      hand: index === ownSeat ? seat.hand : null,
      escaped: seat.escaped,
      escapeRank: seat.escapeRank,
    })),
    trick: state.trick,
    discardCount: state.discard.length,
    openingPending: state.openingPending,
    handTakeUsed: state.handTakeUsed,
    pendingAutoLead: state.pendingAutoLead,
    stats: state.stats,
    escapeOrder: state.escapeOrder,
    bhabhiSeat: state.bhabhiSeat,
    confirmedBhabhi: state.confirmedBhabhi,
    commitment: state.commitment,
    seedHex: ended ? state.seedHex : null,
    deckOrder: ended ? state.deckOrder : null,
    canTakeHand: ownSeat === null ? null : canTakeHand(state, ownSeat),
  };
}

/** Events are public except a private draw, which only the seats it names receive. */
export function redactEvent(event: ThullaEvent, viewer: Viewer): ThullaEvent | null {
  if (event.type !== "private_draw") {
    return event;
  }
  return viewer.kind === "seat" && event.visibleTo.includes(viewer.seat) ? event : null;
}
