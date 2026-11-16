import type { Card, Suit } from "../card-kit";
import type { SeatSpec } from "../contract";
import type { RungRules } from "./rules";
import type { SeatIndex } from "@hulla/shared-ts";

/** RG1: exactly four seats in two pairs sitting opposite. */
export const RUNG_SEATS: SeatSpec = {
  min: 4,
  max: 4,
  teams: { count: 2, size: 2, arrangement: "crosswise" },
};

export const RUNG_SEAT_COUNT = 4;
export const RUNG_TRICKS = 13;
/** RG24: seven of thirteen win the deal. */
export const RUNG_TRICKS_TO_WIN = 7;

export type RungPhase = "call" | "play" | "courtChoice" | "ended";
export type RungTeam = 0 | 1;
export type RungOutcomeKind = "win" | "court" | "court52";

/** RG30: one outcome for one team. */
export interface RungOutcome {
  readonly team: RungTeam;
  readonly kind: RungOutcomeKind;
}

/** Per-seat counters for the results screen and stats. */
export interface RungSeatStats {
  readonly tricksWon: number;
  /** Kaats: rungs played on a led suit the seat was void in. */
  readonly cuts: number;
  readonly pilesTaken: number;
}

export const EMPTY_RUNG_STATS: RungSeatStats = { tricksWon: 0, cuts: 0, pilesTaken: 0 };

export interface RungTrick {
  readonly leaderSeat: SeatIndex;
  readonly plays: readonly { readonly seat: SeatIndex; readonly card: Card }[];
}

/*
 * Everything is readonly; moves return new objects (ARCHITECTURE 2.3). Absent values are `null`,
 * never `undefined`, and every number is an integer, so canonical JSON and the hash are stable.
 */
export interface RungState {
  readonly rulesVersion: string;
  readonly rules: RungRules;
  readonly seatCount: number;
  readonly dealerSeat: SeatIndex;
  /** RG4: the seat after the dealer names the rung and leads. */
  readonly callerSeat: SeatIndex;
  readonly phase: RungPhase;
  /** The seat expected to act; null once the deal has ended. */
  readonly turnSeat: SeatIndex | null;
  /** Always in canonical order, so the hash never depends on arrival order (ARCHITECTURE 7.5). */
  readonly hands: readonly (readonly Card[])[];
  /** RG5: the cards nobody holds yet, in dealing order; empty after the call. Server-only. */
  readonly stock: readonly Card[];
  readonly trump: Suit | null;
  /** RG7: redeals asked for in a row in this deal. */
  readonly redeals: number;
  /** 0 for the first trick, 12 for the last. */
  readonly trickIndex: number;
  readonly trick: RungTrick;
  /** Double Sar: the seat whose run is alive, or null after a take (RG17, RG19). */
  readonly lastWinner: SeatIndex | null;
  /** Tricks in a row won by `lastWinner`. */
  readonly run: number;
  /** RG22: whether `lastWinner` won their latest trick with an ace. */
  readonly lastWinAce: boolean;
  /** RG22: the ace rule has already refused this run once, so its next win takes the pile. */
  readonly runAceBlocked: boolean;
  /** RG16: tricks lying in the centre that nobody owns yet. */
  readonly pile: number;
  /** Tricks per team: seats 0 and 2 are team 0, seats 1 and 3 team 1 (RG1). */
  readonly counts: readonly [number, number];
  /** RG26: the team that chose to play on for the bigger prize. */
  readonly chasing: RungTeam | null;
  readonly outcome: RungOutcome | null;
  readonly stats: readonly RungSeatStats[];
  /** Public before the deal: SHA-256(seed ‖ canonicalJson(deckOrder)); renewed by a redeal. */
  readonly commitment: string;
  /** Server-only until the deal ends, then revealed so clients verify the commitment. */
  readonly seedHex: string;
  readonly deckOrder: readonly Card[];
}

export function teamOf(seat: SeatIndex): RungTeam {
  return seat % 2 === 0 ? 0 : 1;
}

export function otherTeam(team: RungTeam): RungTeam {
  return team === 0 ? 1 : 0;
}

/** RG3: the next seat in play order. Direction is how the table draws it; the engine counts up. */
export function nextRungSeat(seat: SeatIndex): SeatIndex {
  return (seat + 1) % RUNG_SEAT_COUNT;
}

export function partnerOf(seat: SeatIndex): SeatIndex {
  return (seat + 2) % RUNG_SEAT_COUNT;
}

export function withRungStats(
  stats: readonly RungSeatStats[],
  seat: SeatIndex,
  update: (current: RungSeatStats) => RungSeatStats,
): RungSeatStats[] {
  return stats.map((current, index) => (index === seat ? update(current) : current));
}
