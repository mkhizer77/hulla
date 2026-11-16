import { describe, expect, it } from "vitest";

import { BUNDLED_ACHIEVEMENTS } from "../content/achievements";
import { BUNDLED_MISSIONS } from "../content/missions";
import { EMPTY_ACHIEVEMENTS, countRound } from "../services/achievements";
import { allBoards, boardId, isBoardGame } from "../services/leaderboards";
import { metricsFromRound } from "../services/missions";
import { EMPTY_STATS, applyRoundToStats } from "../services/profiles";
import { roundReward } from "../services/progression";
import {
  RUNG_COINS_BAZI_WON,
  RUNG_COINS_COURT,
  RUNG_COINS_DEAL_LOST,
  RUNG_COINS_DEAL_WON,
  RUNG_XP_BAZI_LOST,
  RUNG_XP_BAZI_WON,
  RUNG_XP_COURT,
  RUNG_XP_DEAL_LOST,
  RUNG_XP_DEAL_WON,
  rungDealFacts,
} from "../services/rung_progress";

import type { RoundRecord } from "../match/types";

interface Deal {
  readonly winnerTeam?: 0 | 1;
  readonly kind?: "win" | "court" | "court52";
  readonly callerSeat?: number;
  readonly trump?: string;
  readonly baziWinner?: 0 | 1 | null;
  readonly completed?: boolean;
}

/** A Rung deal's record as the table core stores it. */
function deal(d: Deal = {}): RoundRecord {
  const winnerTeam = d.winnerTeam ?? 0;
  const callerSeat = d.callerSeat ?? 0;
  const baziWinner = d.baziWinner ?? null;
  return {
    tableId: "t_1",
    roundId: "r_1",
    gameId: "rung",
    rulesVersion: "1.0.0",
    rules: { gameId: "rung" },
    mode: "private",
    dealerSeat: (callerSeat + 3) % 4,
    seedHex: "00",
    players: ["u0", "u1", "u2", "u3"],
    moves: [],
    result: {
      gameId: "rung",
      finishOrder: winnerTeam === 0 ? [0, 2, 1, 3] : [1, 3, 0, 2],
      loserSeat: null,
      points: [0, 1, 2, 3].map((seat) => (seat % 2 === winnerTeam ? 2 : 0)),
      winnerTeam,
      kind: d.kind ?? "win",
      byCaller: callerSeat % 2 === winnerTeam,
      callerSeat,
      trump: d.trump ?? "S",
    } as never,
    series: {
      roundsPlayed: 1,
      points: [0, 0, 0, 0],
      loserTally: [0, 0, 0, 0],
      nextDealerSeat: 0,
      finished: baziWinner !== null,
      winnerTeam: baziWinner,
    } as never,
    finalStateHash: "h",
    highlights: [
      { tricksWon: 4, cuts: 1, pilesTaken: 2, calls: 1, redeals: 0 },
      { tricksWon: 2, cuts: 0, pilesTaken: 0, calls: 0, redeals: 0 },
      { tricksWon: 3, cuts: 2, pilesTaken: 1, calls: 0, redeals: 0 },
      { tricksWon: 0, cuts: 0, pilesTaken: 0, calls: 0, redeals: 0 },
    ],
    startedTs: 0,
    endedTs: 1_000,
    completed: d.completed ?? true,
  };
}

const ctx = { withFriend: false, coinsToday: 0, loginStreak: 1 };

describe("Rung rewards (Rung design 10, OQ-056)", () => {
  it("a deal pays the winners about twice the losers", () => {
    const record = deal();
    expect(roundReward(record, 0, ctx)).toMatchObject({
      xp: RUNG_XP_DEAL_WON,
      coins: RUNG_COINS_DEAL_WON,
    });
    expect(roundReward(record, 1, ctx)).toMatchObject({
      xp: RUNG_XP_DEAL_LOST,
      coins: RUNG_COINS_DEAL_LOST,
    });
  });

  it("a court adds a bonus for the pair that scored it, double for a 52-court", () => {
    expect(roundReward(deal({ kind: "court" }), 2, ctx)?.xp).toBe(RUNG_XP_DEAL_WON + RUNG_XP_COURT);
    expect(roundReward(deal({ kind: "court52" }), 2, ctx)?.coins).toBe(
      RUNG_COINS_DEAL_WON + 2 * RUNG_COINS_COURT,
    );
    expect(roundReward(deal({ kind: "court" }), 1, ctx)?.xp).toBe(RUNG_XP_DEAL_LOST);
  });

  it("the deal that ends the bazi pays the bazi bonus to both pairs", () => {
    const last = deal({ baziWinner: 0 });
    expect(roundReward(last, 0, ctx)).toMatchObject({
      xp: RUNG_XP_DEAL_WON + RUNG_XP_BAZI_WON,
      coins: RUNG_COINS_DEAL_WON + RUNG_COINS_BAZI_WON,
    });
    expect(roundReward(last, 3, ctx)?.xp).toBe(RUNG_XP_DEAL_LOST + RUNG_XP_BAZI_LOST);
  });

  it("keeps Thulla's friend bonus, login streak and daily cap", () => {
    const record = deal();
    expect(roundReward(record, 0, { ...ctx, withFriend: true })?.xp).toBe(
      Math.round(RUNG_XP_DEAL_WON * 1.2),
    );
    expect(roundReward(record, 0, { ...ctx, loginStreak: 3 })?.coins).toBe(
      Math.round(RUNG_COINS_DEAL_WON * 1.2),
    );
    expect(roundReward(record, 0, { ...ctx, coinsToday: 1_500 })?.coins).toBe(
      Math.round(RUNG_COINS_DEAL_WON * 0.2),
    );
  });

  it("an abandoned deal pays nothing", () => {
    expect(roundReward(deal({ completed: false }), 0, ctx)).toBeNull();
    expect(rungDealFacts({ ...deal(), result: null }, 0)).toBeNull();
  });
});

describe("Rung stats on the profile", () => {
  it("counts deals, bazis, courts, the call and the rung called, and leaves Thulla's numbers alone", () => {
    let stats = applyRoundToStats(
      EMPTY_STATS,
      0,
      deal({ kind: "court", trump: "H", baziWinner: 0 }),
    );
    stats = applyRoundToStats(stats, 0, deal({ winnerTeam: 1, callerSeat: 1 }));
    expect(stats.rounds).toBe(0);
    expect(stats.rung).toMatchObject({
      deals: 2,
      dealsWon: 1,
      bazis: 1,
      bazisWon: 1,
      courts: 1,
      callerDeals: 1,
      callerWins: 1,
      defenderDeals: 1,
      defenderWins: 0,
      tricksWon: 8,
      cuts: 2,
      pilesTaken: 4,
      trumps: { S: 0, H: 1, C: 0, D: 0 },
    });
    // The partner of the caller did not name the rung.
    expect(applyRoundToStats(EMPTY_STATS, 2, deal({ trump: "H" })).rung?.trumps.H).toBe(0);
    expect(applyRoundToStats(EMPTY_STATS, 1, deal({ kind: "court" })).rung?.courtsAgainst).toBe(1);
  });
});

describe("Rung achievements and missions", () => {
  const facts = { friendsAtTable: 1, playerIds: ["u0", "u1", "u2", "u3"] };

  it("counts courts, goon courts, the 52-court and bazis won with a friend", () => {
    let record = countRound(EMPTY_ACHIEVEMENTS, deal({ kind: "court", baziWinner: 0 }), 0, facts);
    expect(record.counters).toMatchObject({
      rung_deals_won: 1,
      rung_courts: 1,
      rung_goon_courts: 0,
      rung_bazis: 1,
      rung_bazis_won: 1,
      rung_bazis_won_friend: 1,
    });
    // The dealer's team (seat 1's) scores the court: a goon court.
    record = countRound(record, deal({ winnerTeam: 1, kind: "court52", callerSeat: 0 }), 1, facts);
    expect(record.counters).toMatchObject({ rung_goon_courts: 1, rung_court52: 1 });
    expect(record.counters.rounds).toBeUndefined();
  });

  it("the call streak moves only on the caller's own deals and breaks on a lost call", () => {
    let record = EMPTY_ACHIEVEMENTS;
    for (let i = 0; i < 3; i += 1) record = countRound(record, deal(), 0, facts);
    expect(record.counters.rung_call_streak).toBe(3);
    record = countRound(record, deal({ callerSeat: 2 }), 0, facts);
    expect(record.counters.rung_call_streak).toBe(3);
    record = countRound(record, deal({ winnerTeam: 1 }), 0, facts);
    expect(record.counters.rung_call_streak).toBe(0);
  });

  it("missions count deals won, piles, kaats, courts and the bazi", () => {
    expect(metricsFromRound(deal({ kind: "court", baziWinner: 0 }), 0, true)).toEqual({
      rounds_played: 1,
      rounds_with_friend: 1,
      rung_deals_won: 1,
      rung_piles: 2,
      rung_cuts: 1,
      rung_courts: 1,
      rung_bazis_won: 1,
    });
    expect(metricsFromRound(deal(), 1, false)).toMatchObject({ rung_deals_won: 0, rung_piles: 0 });
  });

  it("content carries Rung achievements and missions that name their game", () => {
    const rung = BUNDLED_ACHIEVEMENTS.achievements.filter((a) => a.gameId === "rung");
    expect(rung.map((a) => a.id)).toContain("rung_goon_court");
    expect(rung.length).toBeGreaterThanOrEqual(10);
    expect(rung.every((a) => a.metric.startsWith("rung_"))).toBe(true);
    const missions = [...BUNDLED_MISSIONS.daily, ...BUNDLED_MISSIONS.weekly];
    expect(missions.filter((m) => m.gameId === "rung")).toHaveLength(5);
  });
});

describe("leaderboards per game", () => {
  it("keeps a weekly and a monthly board for Thulla, Rung and both together", () => {
    expect(boardId("thulla", "weekly")).toBe("baithak_weekly");
    expect(boardId("rung", "monthly")).toBe("rung_monthly");
    expect(boardId("all", "weekly")).toBe("all_weekly");
    expect(allBoards()).toHaveLength(6);
    expect(isBoardGame("rung")).toBe(true);
    expect(isBoardGame("ludo")).toBe(false);
  });
});
