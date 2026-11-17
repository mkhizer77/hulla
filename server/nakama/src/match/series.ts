import type { SeriesState } from "@hulla/rules-engine";

/** The player at each index of a series: the seats a round is dealt to, in order. */
export type SeriesPlayers = readonly (string | null)[];

export function sameSeating(a: SeriesPlayers, b: SeriesPlayers): boolean {
  return a.length === b.length && a.every((id, index) => id === b[index]);
}

/**
 * A per-player score moved to where its players sit now (GDD R50, R51): each player keeps their
 * points and tally, a newcomer starts from nothing, and the deal stays with the player who was to
 * deal. Returns the same series when nobody moved, when the seating it was scored for is unknown,
 * or when the number of seats differs (the caller starts a new score then).
 */
export function seriesForPlayers(
  series: SeriesState,
  before: SeriesPlayers | null,
  now: SeriesPlayers,
): SeriesState {
  if (before?.length !== series.points.length) return series;
  if (now.length !== before.length || sameSeating(before, now)) return series;
  const from = now.map((id) => (id === null ? -1 : before.indexOf(id)));
  const dealer = before[series.nextDealerSeat] ?? null;
  const dealerNow = dealer === null ? -1 : now.indexOf(dealer);
  return {
    ...series,
    points: from.map((index) => (index === -1 ? 0 : (series.points[index] ?? 0))),
    loserTally: from.map((index) => (index === -1 ? 0 : (series.loserTally[index] ?? 0))),
    nextDealerSeat: dealerNow === -1 ? series.nextDealerSeat : dealerNow,
  };
}
