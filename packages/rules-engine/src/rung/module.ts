import { rungAutoPlayMove } from "./autoplay";
import { rungBotMove } from "./bot";
import { asRungBotMemory, observeRungBotMemory, rungBotMemory } from "./bot-memory";
import { createRungRound } from "./deal";
import { applyRungMove, currentRungPhase, isRungRoundOver, legalRungMoves } from "./engine";
import { rungResults, rungSeriesUpdate } from "./results";
import { RUNG_RULES_SCHEMA, RUNG_RULES_VERSION, type RungRules } from "./rules";
import { RUNG_SEATS, type RungState } from "./state";
import { redactRung, type RungView } from "./view";

import type { BotPolicy, GameModule, PhaseSpec, RoundResult } from "../contract";
import type { RungEvent } from "./events";
import type { RungMove } from "./moves";

/** RG41: the call, each turn and the court choice are one seat's timer; the seconds are table settings. */
export const RUNG_PHASES: readonly PhaseSpec[] = [
  { id: "call", deadline: { kind: "perSeatTimer" } },
  { id: "play", deadline: { kind: "perSeatTimer" } },
  { id: "courtChoice", deadline: { kind: "perSeatTimer" } },
  { id: "ended", deadline: { kind: "none" } },
];

export type RungModule = GameModule<RungState, RungMove, RungView, RungEvent, RungRules>;

/** Rung design 6: bots play from public facts only; a memory that does not read back starts over. */
export const rungBot: BotPolicy<RungState, RungMove, RungEvent> = {
  start: () => rungBotMemory(),
  observe: (memory, events) =>
    observeRungBotMemory(asRungBotMemory(memory) ?? rungBotMemory(), events),
  choose: (state, seat, memory, rng) => rungBotMove(state, seat, asRungBotMemory(memory), rng),
};

/** The Rung game module the table core registers (ARCHITECTURE 7.1). */
export const rungModule: RungModule = {
  id: "rung",
  version: RUNG_RULES_VERSION,
  seats: RUNG_SEATS,
  // RG3: anticlockwise on the table by default; the engine itself only counts seats up.
  direction: "configurable",
  rulesSchema: RUNG_RULES_SCHEMA,
  phases: RUNG_PHASES,
  createRound: createRungRound,
  currentPhase: currentRungPhase,
  legalMoves: legalRungMoves,
  applyMove: applyRungMove,
  onDeadline: rungAutoPlayMove,
  bot: rungBot,
  redact: redactRung,
  isRoundOver: isRungRoundOver,
  results: (state): RoundResult => {
    const result = rungResults(state);
    if (result === null) {
      throw new Error("invariant: results are read only after isRoundOver");
    }
    return result;
  },
  seriesUpdate: rungSeriesUpdate,
};
