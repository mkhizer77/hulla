import { describe, expect, it } from "vitest";

import { sameSeating, seriesForPlayers } from "../match/series";

import type { SeriesState } from "@hulla/rules-engine";

const SCORE: SeriesState = {
  roundsPlayed: 3,
  points: [10, 20, 30, 40],
  loserTally: [1, 0, 2, 0],
  nextDealerSeat: 3,
  finished: false,
};

describe("a per-player score follows its players", () => {
  it("R50 two players who trade seats keep their own points and tally", () => {
    const moved = seriesForPlayers(SCORE, ["a", "b", "c", "d"], ["a", "d", "c", "b"]);
    expect(moved.points).toEqual([10, 40, 30, 20]);
    expect(moved.loserTally).toEqual([1, 0, 2, 0]);
    expect(moved.roundsPlayed).toBe(3);
    // The deal stays with the player who was to deal, now in seat 1.
    expect(moved.nextDealerSeat).toBe(1);
  });

  it("R50 a newcomer starts from nothing instead of taking over a stranger's score", () => {
    const moved = seriesForPlayers(SCORE, ["a", "b", "c", "d"], ["a", "x", "c", "b"]);
    expect(moved.points).toEqual([10, 0, 30, 20]);
    expect(moved.loserTally).toEqual([1, 0, 2, 0]);
    // The player who was to deal has gone: the seat number keeps the deal.
    expect(moved.nextDealerSeat).toBe(3);
  });

  it("keeps a game's own fields on the series", () => {
    const rich = { ...SCORE, extra: [7, 8] } as SeriesState;
    expect(seriesForPlayers(rich, ["a", "b", "c", "d"], ["d", "c", "b", "a"])).toMatchObject({
      extra: [7, 8],
      points: [40, 30, 20, 10],
    });
  });

  it("returns the same series when nobody moved, the seating is unknown or the count differs", () => {
    expect(seriesForPlayers(SCORE, ["a", "b", "c", "d"], ["a", "b", "c", "d"])).toBe(SCORE);
    expect(seriesForPlayers(SCORE, null, ["a", "b", "c", "d"])).toBe(SCORE);
    expect(seriesForPlayers(SCORE, ["a", "b", "c"], ["a", "b", "c", "d"])).toBe(SCORE);
    expect(seriesForPlayers(SCORE, ["a", "b", "c", "d"], ["a", "b", "c"])).toBe(SCORE);
  });

  it("compares two seatings seat by seat", () => {
    expect(sameSeating(["a", null], ["a", null])).toBe(true);
    expect(sameSeating(["a", "b"], ["b", "a"])).toBe(false);
    expect(sameSeating(["a"], ["a", "b"])).toBe(false);
  });
});
