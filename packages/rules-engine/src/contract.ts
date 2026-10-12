import type { JsonObject, Rng, Result, RuleError, SeatIndex } from "@hulla/shared-ts";

/** Game modules known to the shell; the registry maps these to implementations. */
export type GameId = "thulla" | "rung";

export interface SeatSpec {
  readonly min: number;
  readonly max: number;
  readonly teams?: {
    readonly count: number;
    readonly size: number;
    readonly arrangement: "crosswise" | "adjacent";
  };
}

/** The shape of a phase's clock; the seconds come from table settings, never from the engine. */
export type DeadlinePolicy =
  | { readonly kind: "perSeatTimer" }
  | { readonly kind: "simultaneousWindow" }
  | { readonly kind: "none" };

export interface PhaseSpec {
  readonly id: string;
  readonly deadline: DeadlinePolicy;
}

export type Viewer =
  { readonly kind: "seat"; readonly seat: SeatIndex } | { readonly kind: "spectator" };

export interface CreateRoundInput<R> {
  readonly rules: R;
  /** Seats occupied for this round, 0..seatCount-1 clockwise (GDD R3, R4). */
  readonly seatCount: number;
  readonly dealerSeat: SeatIndex;
  /** The round seed the table core drew; 32 bytes. Revealed to clients when the round ends (R6). */
  readonly seed: Uint8Array;
}

export type ApplyResult<S, E> =
  | { readonly ok: true; readonly state: S; readonly events: readonly E[] }
  | { readonly ok: false; readonly error: RuleError };

export interface RoundResult {
  readonly gameId: GameId;
  /** Seats in finishing order, best first. For Thulla: the escape order then the Bhabhi. */
  readonly finishOrder: readonly SeatIndex[];
  readonly loserSeat: SeatIndex | null;
  readonly points: readonly number[];
}

export interface SeriesState {
  readonly roundsPlayed: number;
  readonly points: readonly number[];
  readonly loserTally: readonly number[];
  readonly nextDealerSeat: SeatIndex;
  readonly finished: boolean;
}

/**
 * The server-side contract every game implements and only the table core consumes
 * (ARCHITECTURE 7.1). Pure functions over immutable state; the only randomness is the injected
 * `Rng`; there is no clock (ADR-0020); nothing throws for bad input.
 */
export interface GameModule<S, M, V, E, R> {
  readonly id: GameId;
  readonly version: string;
  readonly seats: SeatSpec;
  readonly direction: "cw" | "ccw" | "configurable";
  readonly rulesSchema: JsonObject;
  readonly phases: readonly PhaseSpec[];

  createRound(input: CreateRoundInput<R>, rng: Rng): Result<S, RuleError>;
  currentPhase(state: S): string;
  legalMoves(state: S, seat: SeatIndex): readonly M[];
  applyMove(state: S, seat: SeatIndex, move: M, rng: Rng): ApplyResult<S, E>;
  onDeadline(state: S, seat: SeatIndex): M;
  redact(state: S, viewer: Viewer): V;
  isRoundOver(state: S): boolean;
  results(state: S): RoundResult;
  seriesUpdate(series: SeriesState, result: RoundResult): SeriesState;
}
