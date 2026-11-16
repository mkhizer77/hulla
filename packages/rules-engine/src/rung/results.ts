import {
  RUNG_SEAT_COUNT,
  otherTeam,
  partnerOf,
  teamOf,
  type RungOutcomeKind,
  type RungSeatStats,
  type RungState,
  type RungTeam,
} from "./state";

import type { Suit } from "../card-kit";
import type { RoundResult, SeriesState } from "../contract";
import type { RungRules } from "./rules";
import type { SeatIndex } from "@hulla/shared-ts";

/** What a finished deal yields, for the bazi, rewards and stats. It carries the rules it was played under. */
export interface RungRoundResult extends RoundResult {
  readonly gameId: "rung";
  readonly rules: RungRules;
  readonly winnerTeam: RungTeam;
  readonly kind: RungOutcomeKind;
  /** Whether the winners were the caller's team; false is the dealer's team (a goon court when a court). */
  readonly byCaller: boolean;
  readonly counts: readonly [number, number];
  readonly dealerSeat: SeatIndex;
  readonly callerSeat: SeatIndex;
  readonly trump: Suit | null;
  /** RG32: the points this deal is worth to the winners under the rules' points table. */
  readonly dealPoints: number;
  readonly stats: readonly RungSeatStats[];
}

/** RG32: the points table. A 52-court scores as a court here; the bazi ends on it (see `rungSeriesUpdate`). */
export function rungDealPoints(rules: RungRules, kind: RungOutcomeKind, byCaller: boolean): number {
  const court = kind !== "win";
  if (rules.pointsTable === "hokm") {
    if (!court) return 1;
    return byCaller ? 2 : 3;
  }
  if (!court) return byCaller ? 1 : 2;
  return byCaller ? 3 : 4;
}

/** RG33: what the outcome adds to the courts tally. */
export function rungCourtsWon(rules: RungRules, kind: RungOutcomeKind, byCaller: boolean): number {
  if (kind === "court52") return 52;
  if (kind === "court") return byCaller ? 1 : rules.goonCourt;
  return 0;
}

export function rungResults(state: RungState): RungRoundResult | null {
  if (state.phase !== "ended" || state.outcome === null) {
    return null;
  }
  const { team, kind } = state.outcome;
  const byCaller = teamOf(state.callerSeat) === team;
  const dealPoints = rungDealPoints(state.rules, kind, byCaller);
  const seats = Array.from({ length: RUNG_SEAT_COUNT }, (_, seat) => seat);
  return {
    gameId: "rung",
    finishOrder: [
      ...seats.filter((seat) => teamOf(seat) === team),
      ...seats.filter((seat) => teamOf(seat) !== team),
    ],
    loserSeat: null,
    points: seats.map((seat) => (teamOf(seat) === team ? dealPoints : 0)),
    rules: state.rules,
    winnerTeam: team,
    kind,
    byCaller,
    counts: state.counts,
    dealerSeat: state.dealerSeat,
    callerSeat: state.callerSeat,
    trump: state.trump,
    dealPoints,
    stats: state.stats,
  };
}

/** A bazi's score. Plain data on top of the shared series state, so the table core stores it as is. */
export interface RungSeriesState extends SeriesState {
  readonly teamPoints: readonly [number, number];
  readonly courts: readonly [number, number];
  readonly dealsWon: readonly [number, number];
  /** RG33: the team on a run of won deals, and its length. */
  readonly streakTeam: RungTeam | null;
  readonly streakDeals: number;
  readonly winnerTeam: RungTeam | null;
}

/** RG33: seven deals in a row are a court. */
export const STREAK_FOR_COURT = 7;

export function createRungSeries(firstDealer: SeatIndex): RungSeriesState {
  return {
    roundsPlayed: 0,
    points: [0, 0, 0, 0],
    loserTally: [0, 0, 0, 0],
    nextDealerSeat: firstDealer,
    finished: false,
    teamPoints: [0, 0],
    courts: [0, 0],
    dealsWon: [0, 0],
    streakTeam: null,
    streakDeals: 0,
    winnerTeam: null,
  };
}

/**
 * RG35 to RG37: the dealer's team won, the old caller deals; the caller's team won, the same
 * dealer deals; the caller's team scored a court, the dealer's partner deals when the rule is on.
 */
export function nextRungDealer(result: RungRoundResult): SeatIndex {
  if (!result.byCaller) {
    return result.callerSeat;
  }
  if (result.kind !== "win" && result.rules.courtPassesCall) {
    return partnerOf(result.dealerSeat);
  }
  return result.dealerSeat;
}

function isRungResult(result: RoundResult): result is RungRoundResult {
  return result.gameId === "rung" && "winnerTeam" in result && "rules" in result;
}

function isRungSeries(series: SeriesState): series is RungSeriesState {
  return "teamPoints" in series && "courts" in series && "dealsWon" in series;
}

function bump(pair: readonly [number, number], team: RungTeam, by: number): [number, number] {
  return team === 0 ? [pair[0] + by, pair[1]] : [pair[0], pair[1] + by];
}

/** RG32 to RG37: the bazi after one more deal. A series or result of another shape comes back unchanged. */
export function rungSeriesUpdate(series: SeriesState, result: RoundResult): SeriesState {
  if (!isRungResult(result)) {
    return series;
  }
  const before: RungSeriesState = isRungSeries(series)
    ? series
    : { ...createRungSeries(series.nextDealerSeat), roundsPlayed: series.roundsPlayed };
  const team = result.winnerTeam;
  const rules = result.rules;
  const dealsWon = bump(before.dealsWon, team, 1);
  let courts = bump(before.courts, team, rungCourtsWon(rules, result.kind, result.byCaller));
  let teamPoints = before.teamPoints;
  let winnerTeam: RungTeam | null = null;
  let streakTeam: RungTeam | null = before.streakTeam;
  let streakDeals = before.streakDeals;

  if (rules.scoring === "points") {
    teamPoints = bump(before.teamPoints, team, result.dealPoints);
    // RG32: a 52-court wins the bazi outright; RG34: otherwise the first to the target.
    if (result.kind === "court52" || teamPoints[team] >= rules.target) {
      winnerTeam = team;
    }
  } else {
    // RG33: seven plain wins in a row are a court; a court starts the count again.
    streakDeals = result.kind === "win" ? (streakTeam === team ? streakDeals + 1 : 1) : 0;
    streakTeam = result.kind === "win" ? team : null;
    if (streakDeals >= STREAK_FOR_COURT) {
      courts = bump(courts, team, 1);
      streakDeals = 0;
      streakTeam = null;
    }
  }

  const shown = rules.scoring === "points" ? teamPoints : courts;
  const next: RungSeriesState = {
    roundsPlayed: before.roundsPlayed + 1,
    points: before.points.map((_, seat) => shown[teamOf(seat)]),
    loserTally: before.loserTally.map((count, seat) =>
      winnerTeam !== null && teamOf(seat) === otherTeam(winnerTeam) ? count + 1 : count,
    ),
    nextDealerSeat: nextRungDealer(result),
    finished: winnerTeam !== null,
    teamPoints,
    courts,
    dealsWon,
    streakTeam,
    streakDeals,
    winnerTeam,
  };
  return next;
}
