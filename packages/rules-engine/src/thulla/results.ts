import { nextDealer } from "./deal";

import type { RoundResult, SeriesState } from "../contract";
import type { SeriesFormat } from "./rules";
import type { SeatStats, ThullaState } from "./state";
import type { SeatIndex } from "@hulla/shared-ts";

/** R49: what a finished round yields, for rewards, stats and the series. */
export interface ThullaRoundResult extends RoundResult {
  readonly gameId: "thulla";
  readonly escapeOrder: readonly SeatIndex[];
  readonly bhabhiSeat: SeatIndex;
  /** Cards the Bhabhi held at the end, for the bigBhabhiPenalty toggle and the share card. */
  readonly bhabhiCards: number;
  readonly stats: readonly SeatStats[];
}

/** R50: 1st escape +3, 2nd +2, 3rd +1, others 0, Bhabhi −1, or −2 with 8 or more cards when toggled. */
export function results(state: ThullaState): ThullaRoundResult | null {
  if (state.phase !== "ended" || state.bhabhiSeat === null) {
    return null;
  }
  const bhabhiSeat = state.bhabhiSeat;
  const bhabhiCards = state.seats[bhabhiSeat]?.hand.length ?? 0;
  const points = state.seats.map(() => 0);
  state.escapeOrder.forEach((seat, index) => {
    points[seat] = Math.max(0, 3 - index);
  });
  points[bhabhiSeat] = state.rules.bigBhabhiPenalty && bhabhiCards >= 8 ? -2 : -1;
  return {
    gameId: "thulla",
    finishOrder: [...state.escapeOrder, bhabhiSeat],
    loserSeat: bhabhiSeat,
    points,
    escapeOrder: state.escapeOrder,
    bhabhiSeat,
    bhabhiCards,
    stats: state.stats,
  };
}

export function createSeries(seatCount: number, firstDealer: SeatIndex): SeriesState {
  return {
    roundsPlayed: 0,
    points: Array.from({ length: seatCount }, () => 0),
    loserTally: Array.from({ length: seatCount }, () => 0),
    nextDealerSeat: firstDealer,
    finished: false,
  };
}

const BEST_OF: Partial<Record<SeriesFormat, number>> = { bestOf3: 3, bestOf5: 5, bestOf7: 7 };

/** R51: single round, best of N by points, first to three Bhabhis loses, or an open session. */
export function seriesUpdate(
  series: SeriesState,
  result: RoundResult,
  format: SeriesFormat,
): SeriesState {
  const points = series.points.map((total, seat) => total + (result.points[seat] ?? 0));
  const loserTally = series.loserTally.map((count, seat) =>
    seat === result.loserSeat ? count + 1 : count,
  );
  const roundsPlayed = series.roundsPlayed + 1;
  return {
    roundsPlayed,
    points,
    loserTally,
    nextDealerSeat: nextDealer(series.nextDealerSeat, series.points.length),
    finished: isFinished(format, roundsPlayed, loserTally),
  };
}

function isFinished(
  format: SeriesFormat,
  roundsPlayed: number,
  loserTally: readonly number[],
): boolean {
  switch (format) {
    case "single":
      return true;
    case "firstTo3Bhabhis":
      return loserTally.some((count) => count >= 3);
    case "session":
      return false;
    default:
      return roundsPlayed >= (BEST_OF[format] ?? 1);
  }
}
