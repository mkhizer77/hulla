import { canRedeal } from "./legal";

import type { Card, Suit } from "../card-kit";
import type { Viewer } from "../contract";
import type { RungRules } from "./rules";
import type {
  RungOutcome,
  RungPhase,
  RungSeatStats,
  RungState,
  RungTeam,
  RungTrick,
} from "./state";
import type { SeatIndex } from "@hulla/shared-ts";

/** What a seat or spectator may know. The stock and other hands never appear (RG5, RGE12, RGE13). */
export interface RungView {
  readonly viewer: Viewer;
  readonly rulesVersion: string;
  readonly rules: RungRules;
  readonly seatCount: number;
  readonly dealerSeat: SeatIndex;
  readonly callerSeat: SeatIndex;
  readonly phase: RungPhase;
  readonly turnSeat: SeatIndex | null;
  readonly handCounts: readonly number[];
  /** The viewer's own cards; null for spectators. */
  readonly hand: readonly Card[] | null;
  readonly stockCount: number;
  readonly trump: Suit | null;
  readonly redeals: number;
  readonly trickIndex: number;
  readonly trick: RungTrick;
  readonly lastWinner: SeatIndex | null;
  readonly run: number;
  readonly pile: number;
  readonly counts: readonly [number, number];
  readonly chasing: RungTeam | null;
  readonly outcome: RungOutcome | null;
  readonly stats: readonly RungSeatStats[];
  readonly commitment: string;
  /** Revealed when the deal has ended so anyone can verify the commitment. */
  readonly seedHex: string | null;
  readonly deckOrder: readonly Card[] | null;
  /** Whether the viewer may ask for a redeal right now (null for spectators). */
  readonly canRedeal: boolean | null;
}

/** Total over every state and viewer; hidden information never leaves here (ARCHITECTURE 7.1). */
export function redactRung(state: RungState, viewer: Viewer): RungView {
  const ownSeat = viewer.kind === "seat" ? viewer.seat : null;
  const ended = state.phase === "ended";
  return {
    viewer,
    rulesVersion: state.rulesVersion,
    rules: state.rules,
    seatCount: state.seatCount,
    dealerSeat: state.dealerSeat,
    callerSeat: state.callerSeat,
    phase: state.phase,
    turnSeat: state.turnSeat,
    handCounts: state.hands.map((hand) => hand.length),
    hand: ownSeat === null ? null : (state.hands[ownSeat] ?? null),
    stockCount: state.stock.length,
    trump: state.trump,
    redeals: state.redeals,
    trickIndex: state.trickIndex,
    trick: state.trick,
    lastWinner: state.lastWinner,
    run: state.run,
    pile: state.pile,
    counts: state.counts,
    chasing: state.chasing,
    outcome: state.outcome,
    stats: state.stats,
    commitment: state.commitment,
    seedHex: ended ? state.seedHex : null,
    deckOrder: ended ? state.deckOrder : null,
    canRedeal: ownSeat === null ? null : canRedeal(state, ownSeat),
  };
}
