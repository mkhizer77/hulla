import { autoPlayMove } from "./autoplay";
import { createThullaRound } from "./deal";
import { applyMove, currentPhase, isRoundOver, legalMoves } from "./engine";
import { results, seriesUpdate } from "./results";
import { THULLA_RULES_SCHEMA, THULLA_RULES_VERSION, type ThullaRules } from "./rules";
import { THULLA_SEATS, type ThullaState } from "./state";
import { redact, type ThullaView } from "./view";

import type { ThullaEvent } from "./events";
import type { ThullaMove } from "./moves";
import type { GameModule, PhaseSpec, RoundResult, SeriesState } from "../contract";
import type { Rng, SeatIndex } from "@hulla/shared-ts";

/** R43: the opening trick is one simultaneous window; every later phase is a per-seat timer. */
export const THULLA_PHASES: readonly PhaseSpec[] = [
  { id: "opening", deadline: { kind: "simultaneousWindow" } },
  { id: "play", deadline: { kind: "perSeatTimer" } },
  { id: "ended", deadline: { kind: "none" } },
];

/** R44: the deterministic auto-play policy for a timeout or an absent seat. */
export function onDeadline(state: ThullaState, seat: SeatIndex): ThullaMove | null {
  return autoPlayMove(state, seat);
}

export type ThullaModule = GameModule<
  ThullaState,
  ThullaMove,
  ThullaView,
  ThullaEvent,
  ThullaRules
>;

/** The Thulla game module the table core registers (ARCHITECTURE 7.1). */
export const thullaModule: ThullaModule = {
  id: "thulla",
  version: THULLA_RULES_VERSION,
  seats: THULLA_SEATS,
  direction: "cw",
  rulesSchema: THULLA_RULES_SCHEMA,
  phases: THULLA_PHASES,
  createRound: (input, rng: Rng) => createThullaRound(input, rng),
  currentPhase,
  legalMoves,
  applyMove,
  onDeadline,
  redact,
  isRoundOver,
  results: (state): RoundResult => {
    const result = results(state);
    if (result === null) {
      throw new Error("invariant: results are read only after isRoundOver");
    }
    return result;
  },
  seriesUpdate: (series: SeriesState, result: RoundResult): SeriesState =>
    seriesUpdate(series, result, "session"),
};
