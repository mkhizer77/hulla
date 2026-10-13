import { describe, expect, it } from "vitest";

import { RATING, expectedScore, kFactor, placesFromFinish, rateRound } from "../elo";

const seasoned = (id: string, rating: number, place: number) => ({
  id,
  rating,
  rounds: 50,
  place,
});

describe("ranked rating", () => {
  it("R53 compares every pair: equal ratings in four seats move by the place", () => {
    const changes = rateRound([
      seasoned("a", 1200, 0),
      seasoned("b", 1200, 1),
      seasoned("c", 1200, 2),
      seasoned("d", 1200, 3),
    ]);
    // K = 32/3 per pair; first out wins three pairs, the Bhabhi loses three.
    expect(changes.map((c) => c.delta)).toEqual([16, 5, -5, -16]);
    expect(changes.reduce((sum, c) => sum + c.delta, 0)).toBe(0);
  });

  it("R53 uses K = 32/(N-1) for N rated players", () => {
    expect(kFactor(2, 50)).toBe(32);
    expect(kFactor(4, 50)).toBeCloseTo(32 / 3);
    expect(kFactor(6, 50)).toBeCloseTo(6.4);
    expect(kFactor(1, 50)).toBe(0);
  });

  it("R53 doubles K for the first ten rounds", () => {
    expect(kFactor(4, 0)).toBeCloseTo((32 / 3) * 2);
    expect(kFactor(4, RATING.provisionalRounds - 1)).toBeCloseTo((32 / 3) * 2);
    expect(kFactor(4, RATING.provisionalRounds)).toBeCloseTo(32 / 3);
    const [fresh] = rateRound([
      { id: "new", rating: 1200, rounds: 0, place: 0 },
      seasoned("old", 1200, 1),
    ]);
    expect(fresh?.delta).toBe(32);
    expect(fresh?.provisional).toBe(true);
  });

  it("R53 the Bhabhi loses to everyone, and an upset pays more than a favourite's win", () => {
    const upset = rateRound([seasoned("low", 1000, 0), seasoned("high", 1400, 1)]);
    const expected = rateRound([seasoned("low", 1000, 1), seasoned("high", 1400, 0)]);
    expect(upset[0]?.delta).toBeGreaterThan(expected[1]?.delta ?? 0);
    const bhabhi = rateRound([
      seasoned("a", 1500, 0),
      seasoned("b", 1500, 1),
      seasoned("c", 1500, 2),
    ]);
    expect(bhabhi[2]?.delta).toBeLessThan(0);
  });

  it("R53 leaves bot seats out of the comparisons", () => {
    const changes = rateRound([
      seasoned("a", 1200, 1),
      { id: "bot", rating: 1200, rounds: 0, place: 0, bot: true },
      seasoned("b", 1200, 2),
    ]);
    expect(changes[1]?.delta).toBe(0);
    // Two rated players: K = 32, and the bot's escape changes nothing between them.
    expect(changes[0]?.delta).toBe(16);
    expect(changes[2]?.delta).toBe(-16);
  });

  it("R53 equal places draw the pair", () => {
    const changes = rateRound([seasoned("a", 1200, 0), seasoned("b", 1200, 0)]);
    expect(changes.map((c) => c.delta)).toEqual([0, 0]);
  });

  it("R53 nobody moves with fewer than two rated players", () => {
    expect(rateRound([seasoned("a", 1200, 0)])[0]?.delta).toBe(0);
  });

  it("R53 places follow the finish order", () => {
    expect(placesFromFinish([2, 0, 3, 1], 4)).toEqual([1, 3, 0, 2]);
    expect(placesFromFinish([1], 3)).toEqual([1, 0, 1]);
    expect(expectedScore(1200, 1200)).toBe(0.5);
  });
});
