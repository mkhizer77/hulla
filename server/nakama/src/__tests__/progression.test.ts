import { describe, expect, it } from "vitest";

import {
  applyReward,
  COINS_PER_LEVEL,
  DAILY_SOFT_CAP,
  EMPTY_PROGRESS,
  forDay,
  levelFor,
  levelGrants,
  localDay,
  roundReward,
  touchLogin,
  xpToAdvance,
  type Progress,
} from "../services/progression";

import type { RoundRecord } from "../match/types";

const T0 = Date.UTC(2026, 9, 13, 12, 0, 0);
const HOUR = 60 * 60 * 1000;

/** A four-seat round: seat 0 escaped first, seat 1 second, seat 2 third, seat 3 is the Bhabhi. */
function record(highlights: Record<string, number>[] = [{}, {}, {}, {}]): RoundRecord {
  return {
    tableId: "t_ABCDEF",
    roundId: "r_1",
    gameId: "thulla",
    rulesVersion: "1",
    rules: { gameId: "thulla" },
    mode: "private",
    dealerSeat: 0,
    seedHex: "00",
    players: ["u0", "u1", "u2", "u3"],
    moves: [],
    result: { gameId: "thulla", finishOrder: [0, 1, 2], loserSeat: 3, points: [3, 2, 1, 0] },
    series: null,
    finalStateHash: "h",
    highlights,
    startedTs: T0,
    endedTs: T0 + 1000,
    completed: true,
  };
}

const plain = { withFriend: false, coinsToday: 0, loginStreak: 1 };

describe("XP and coins per round (GDD 12.2, 12.3)", () => {
  it("pays the finish positions and the Bhabhi from the GDD tables", () => {
    const r = record();
    expect(roundReward(r, 0, plain)).toMatchObject({
      xp: 50,
      coins: 60,
      breakdown: { finish: 0, cleanXp: 10 },
    });
    expect(roundReward(r, 1, plain)).toMatchObject({ xp: 40, coins: 45 });
    expect(roundReward(r, 2, plain)).toMatchObject({ xp: 30, coins: 35 });
    expect(roundReward(r, 3, plain)).toMatchObject({
      xp: 10,
      coins: 15,
      breakdown: { bhabhi: true, cleanXp: 0 },
    });
    expect(roundReward({ ...r, completed: false }, 0, plain)).toBeNull();
  });

  it("adds thullas (capped), the clean round and the friend bonus: the worked example", () => {
    // Seat 1 escaped second with three thullas given and two cards picked up, playing with a friend:
    // 30 + 6 = 36, +20% = 43 XP; 45 coins.
    const r = record([{}, { thullasGiven: 3, cardsPickedUp: 2 }, {}, {}]);
    expect(roundReward(r, 1, { ...plain, withFriend: true })).toEqual({
      xp: 43,
      coins: 45,
      breakdown: {
        finish: 1,
        bhabhi: false,
        baseXp: 30,
        thullaXp: 6,
        cleanXp: 0,
        friendXp: 7,
        baseCoins: 45,
        streakCoins: 0,
        capped: false,
      },
    });
    const many = record([{ thullasGiven: 9, cardsPickedUp: 0 }, {}, {}, {}]);
    expect(roundReward(many, 0, plain)).toMatchObject({
      xp: 60,
      breakdown: { thullaXp: 10, cleanXp: 10 },
    });
  });

  it("applies the login streak and the daily soft cap", () => {
    const r = record();
    expect(roundReward(r, 0, { ...plain, loginStreak: 3 })).toMatchObject({
      coins: 72,
      breakdown: { streakCoins: 12 },
    });
    expect(roundReward(r, 0, { ...plain, loginStreak: 9 })).toMatchObject({
      coins: 90,
      breakdown: { streakCoins: 30 },
    });
    expect(roundReward(r, 0, { ...plain, coinsToday: DAILY_SOFT_CAP })).toMatchObject({
      coins: 12,
      breakdown: { capped: true },
    });
    expect(roundReward(r, 0, { ...plain, coinsToday: DAILY_SOFT_CAP - 1 })).toMatchObject({
      coins: 60,
    });
  });
});

describe("levels, titles and grants (GDD 12.2)", () => {
  it("follows 100 + 25n and the milestones", () => {
    expect(xpToAdvance(1)).toBe(125);
    expect(levelFor(0)).toEqual({ level: 1, intoLevel: 0, toNext: 125 });
    expect(levelFor(124)).toMatchObject({ level: 1, intoLevel: 124 });
    expect(levelFor(125)).toEqual({ level: 2, intoLevel: 0, toNext: 150 });
    // 100 + 25n summed over levels 1 to 9 is 2,025: "level 10 at about 2,000".
    expect(levelFor(2_024).level).toBe(9);
    expect(levelFor(2_025).level).toBe(10);
    expect(levelFor(35_525).level).toBe(50);
    expect(levelFor(36_000).level).toBeGreaterThanOrEqual(49);
    expect(levelFor(10_000_000).level).toBe(100);
    expect(levelGrants(9, 10)).toEqual([
      { level: 10, coins: COINS_PER_LEVEL, gems: 20, title: "khiladi" },
    ]);
    expect(levelGrants(3, 5).map((g) => [g.level, g.gems, g.title])).toEqual([
      [4, 0, null],
      [5, 10, null],
    ]);
  });

  it("applies a reward: XP, level-ups, new titles and today's coins", () => {
    const start: Progress = {
      ...EMPTY_PROGRESS,
      xp: 120,
      day: localDay(T0, 300),
      loginStreak: 1,
      lastLoginDay: localDay(T0, 300),
    };
    const applied = applyReward(
      start,
      {
        xp: 50,
        coins: 60,
        breakdown: roundReward(record(), 0, plain)?.breakdown ?? {
          finish: 0,
          bhabhi: false,
          baseXp: 40,
          thullaXp: 0,
          cleanXp: 10,
          friendXp: 0,
          baseCoins: 60,
          streakCoins: 0,
          capped: false,
        },
      },
      { tableId: "t", roundId: "r_1" },
      T0,
    );
    expect(applied.progress).toMatchObject({
      xp: 170,
      level: 2,
      coinsToday: 60,
      titles: ["naya_khiladi"],
    });
    expect(applied.grants).toEqual([{ level: 2, coins: COINS_PER_LEVEL, gems: 0, title: null }]);
    expect(applied.reward).toMatchObject({ levelFrom: 1, levelTo: 2, coins: 60, xp: 50 });
    const big = applyReward(
      { ...start, xp: 1_990 },
      { xp: 50, coins: 10, breakdown: applied.reward.breakdown },
      { tableId: "t", roundId: "r_2" },
      T0,
    );
    expect(big.progress.titles).toEqual(["naya_khiladi", "khiladi"]);
    expect(big.reward.titles).toEqual(["khiladi"]);
  });
});

describe("local midnight (GDD 12.3)", () => {
  it("rolls the daily counters and the streak at the player's midnight, not UTC", () => {
    // 23:30 in Karachi (UTC+5) is 18:30 UTC; the next local day starts 30 minutes later.
    const late = Date.UTC(2026, 9, 13, 18, 30);
    const p: Progress = {
      ...EMPTY_PROGRESS,
      tzOffsetMinutes: 300,
      day: localDay(late, 300),
      coinsToday: 900,
    };
    expect(localDay(late, 300)).toBe("2026-10-13");
    expect(forDay(p, late + 29 * 60_000).coinsToday).toBe(900);
    expect(forDay(p, late + 31 * 60_000)).toMatchObject({ day: "2026-10-14", coinsToday: 0 });
    const first = touchLogin(p, late);
    expect(first).toMatchObject({ loginStreak: 1, lastLoginDay: "2026-10-13" });
    expect(touchLogin(first, late + 10 * 60_000)).toBe(first);
    expect(touchLogin(first, late + 40 * 60_000)).toMatchObject({
      loginStreak: 2,
      lastLoginDay: "2026-10-14",
    });
    expect(touchLogin(first, late + 3 * 24 * HOUR)).toMatchObject({
      loginStreak: 1,
      lastLoginDay: "2026-10-16",
    });
  });
});
