import { describe, expect, it } from "vitest";

import { rungModule } from "../module";
import {
  createRungSeries,
  nextRungDealer,
  rungFirstDealer,
  rungResults,
  rungSeriesAfterSwitch,
  rungSeriesUpdate,
  type RungRoundResult,
  type RungSeriesState,
} from "../results";

import { SINGLE, endedWith, stateFrom } from "./helpers";

import type { RungRules } from "../rules";
import type { RungOutcome } from "../state";

function result(outcome: RungOutcome, rules: Partial<RungRules> = {}): RungRoundResult {
  const value = rungResults(endedWith(outcome, rules));
  if (value === null) {
    throw new Error("expected a result");
  }
  return value;
}

function after(outcomes: readonly RungOutcome[], rules: Partial<RungRules> = {}): RungSeriesState {
  let series = createRungSeries(3);
  for (const outcome of outcomes) {
    series = rungSeriesUpdate(series, result(outcome, rules)) as RungSeriesState;
  }
  return series;
}

const WIN_A: RungOutcome = { team: 0, kind: "win" };
const WIN_B: RungOutcome = { team: 1, kind: "win" };
const COURT_A: RungOutcome = { team: 0, kind: "court" };
const COURT_B: RungOutcome = { team: 1, kind: "court" };

describe("deal results", () => {
  it("RG30_a_deal_has_one_outcome_for_one_team_and_none_before_it_ends", () => {
    expect(
      rungResults(stateFrom({ hands: [["2H"], ["3H"], ["4H"], ["5H"]], trump: "S" })),
    ).toBeNull();
    expect(() => rungModule.results(stateFrom({ hands: [[], [], [], []], trump: "S" }))).toThrow();
    const value = result(COURT_B);
    expect(value).toMatchObject({
      gameId: "rung",
      winnerTeam: 1,
      kind: "court",
      byCaller: false,
      loserSeat: null,
      finishOrder: [1, 3, 0, 2],
      dealerSeat: 3,
      callerSeat: 0,
      trump: "S",
    });
  });

  it("RG32_points_for_the_rung_table_are_one_two_three_and_four", () => {
    expect(result(WIN_A).points).toEqual([1, 0, 1, 0]);
    expect(result(WIN_B).points).toEqual([0, 2, 0, 2]);
    expect(result(COURT_A).points).toEqual([3, 0, 3, 0]);
    expect(result(COURT_B).points).toEqual([0, 4, 0, 4]);
    expect(result({ team: 0, kind: "court52" }, SINGLE).dealPoints).toBe(3);
  });

  it("RG32_points_for_the_hokm_table_are_one_one_two_and_three", () => {
    const hokm: Partial<RungRules> = { ...SINGLE, pointsTable: "hokm" };
    expect(result(WIN_A, hokm).dealPoints).toBe(1);
    expect(result(WIN_B, hokm).dealPoints).toBe(1);
    expect(result(COURT_A, hokm).dealPoints).toBe(2);
    expect(result(COURT_B, hokm).dealPoints).toBe(3);
  });
});

describe("the bazi", () => {
  it("RG38_a_new_bazi_starts_level_with_the_dealer_it_was_given", () => {
    expect(createRungSeries(2)).toMatchObject({
      roundsPlayed: 0,
      nextDealerSeat: 2,
      finished: false,
      teamPoints: [0, 0],
      winnerTeam: null,
    });
  });

  it("RG4_RG38_the_first_dealer_of_a_bazi_is_drawn_from_the_round_seed", () => {
    const seed = (fill: number): Uint8Array => new Uint8Array(32).fill(fill);
    // The same seed always names the same dealer, so a replay deals the same bazi.
    expect(rungFirstDealer(seed(7))).toBe(rungFirstDealer(seed(7)));
    const drawn = Array.from({ length: 64 }, (_, fill) => rungFirstDealer(seed(fill)));
    expect(drawn.every((seat) => Number.isInteger(seat) && seat >= 0 && seat < 4)).toBe(true);
    // Every seat deals first for some seed: the host seat has no claim on it.
    expect(new Set(drawn).size).toBe(4);
  });

  it("RG34_the_first_team_to_the_target_wins_the_bazi", () => {
    const rules = { target: 5 } as const;
    const four = after([WIN_A, COURT_A], rules);
    expect(four.teamPoints).toEqual([4, 0]);
    expect(four.finished).toBe(false);
    expect(four.points).toEqual([4, 0, 4, 0]);
    const done = after([WIN_A, COURT_A, WIN_B, WIN_A], rules);
    expect(done.teamPoints).toEqual([5, 2]);
    expect(done.finished).toBe(true);
    expect(done.winnerTeam).toBe(0);
    expect(done.roundsPlayed).toBe(4);
    expect(done.dealsWon).toEqual([3, 1]);
    expect(done.loserTally).toEqual([0, 1, 0, 1]);
  });

  it("RG32_a_52_court_wins_the_bazi_outright", () => {
    const series = after([{ team: 0, kind: "court52" }], { ...SINGLE, target: 10 });
    expect(series.finished).toBe(true);
    expect(series.winnerTeam).toBe(0);
  });

  it("RGE10_the_score_changes_only_when_a_deal_has_ended", () => {
    const series = createRungSeries(3);
    const thulla = { gameId: "thulla", finishOrder: [], loserSeat: null, points: [] } as const;
    expect(rungSeriesUpdate(series, thulla)).toBe(series);
    expect(rungResults(stateFrom({ hands: [[], [], [], []], trump: "S", chasing: 0 }))).toBeNull();
  });

  it("RG33_the_courts_tally_counts_a_court_a_goon_court_and_a_52_court", () => {
    const courts = { scoring: "courts" } as const;
    expect(after([COURT_A], courts).courts).toEqual([1, 0]);
    expect(after([COURT_B], courts).courts).toEqual([0, 3]);
    expect(after([COURT_B], { ...courts, goonCourt: 10 }).courts).toEqual([0, 10]);
    expect(after([{ team: 0, kind: "court52" }], { ...courts, ...SINGLE }).courts).toEqual([52, 0]);
    const open = after([WIN_A, COURT_B, WIN_A], courts);
    expect(open.finished).toBe(false);
    expect(open.points).toEqual([0, 3, 0, 3]);
    expect(open.teamPoints).toEqual([0, 0]);
  });

  it("RG33_seven_deals_won_in_a_row_are_a_court_and_the_run_starts_again", () => {
    const courts = { scoring: "courts" } as const;
    const six = after(
      Array.from({ length: 6 }, () => WIN_A),
      courts,
    );
    expect(six.courts).toEqual([0, 0]);
    expect(six.streakDeals).toBe(6);
    const seven = after(
      Array.from({ length: 7 }, () => WIN_A),
      courts,
    );
    expect(seven.courts).toEqual([1, 0]);
    expect(seven.streakDeals).toBe(0);
    expect(
      after(
        Array.from({ length: 8 }, () => WIN_A),
        courts,
      ).streakDeals,
    ).toBe(1);
  });

  it("RGE14_the_run_of_seven_is_broken_by_the_other_team_and_by_a_court", () => {
    const courts = { scoring: "courts" } as const;
    const broken = after([WIN_A, WIN_A, WIN_B], courts);
    expect(broken.streakTeam).toBe(1);
    expect(broken.streakDeals).toBe(1);
    const court = after([WIN_A, WIN_A, COURT_A, WIN_A], courts);
    expect(court.streakDeals).toBe(1);
    // A new bazi starts the count again.
    expect(createRungSeries(0).streakDeals).toBe(0);
  });

  it("RGE14_the_run_of_seven_does_not_carry_across_a_game_switch", () => {
    const courts = { scoring: "courts" } as const;
    const run = after([WIN_A, WIN_A, WIN_A, COURT_B], courts);
    const six = after(
      Array.from({ length: 6 }, () => WIN_A),
      courts,
    );
    expect(six.streakDeals).toBe(6);
    const back = rungSeriesAfterSwitch(six) as RungSeriesState;
    expect(back.streakTeam).toBeNull();
    expect(back.streakDeals).toBe(0);
    // The score itself stays.
    expect(back.dealsWon).toEqual(six.dealsWon);
    expect(back.courts).toEqual(six.courts);
    expect(back.roundsPlayed).toBe(6);
    expect((rungSeriesAfterSwitch(run) as RungSeriesState).courts).toEqual(run.courts);
    // The next win after the switch is the first of a new run, not the seventh.
    const next = rungSeriesUpdate(back, result(WIN_A, courts)) as RungSeriesState;
    expect(next.streakDeals).toBe(1);
    expect(next.courts).toEqual(six.courts);
    // Another game's score is left alone.
    const plain = {
      roundsPlayed: 1,
      points: [1, 0, 0],
      loserTally: [0, 0, 1],
      nextDealerSeat: 1,
      finished: false,
    };
    expect(rungSeriesAfterSwitch(plain)).toBe(plain);
  });

  it("starts a Rung score when it is handed a plain series", () => {
    const plain = {
      roundsPlayed: 2,
      points: [0, 0, 0, 0],
      loserTally: [0, 0, 0, 0],
      nextDealerSeat: 3,
      finished: false,
    };
    const next = rungModule.seriesUpdate(plain, result(WIN_B)) as RungSeriesState;
    expect(next.roundsPlayed).toBe(3);
    expect(next.teamPoints).toEqual([0, 2]);
  });
});

describe("who deals next", () => {
  it("RG35_the_dealers_team_won_so_the_old_caller_deals", () => {
    expect(nextRungDealer(result(WIN_B))).toBe(0);
    expect(nextRungDealer(result(COURT_B))).toBe(0);
    expect(after([WIN_B]).nextDealerSeat).toBe(0);
  });

  it("RG36_the_callers_team_won_without_a_court_so_the_same_dealer_deals", () => {
    expect(nextRungDealer(result(WIN_A))).toBe(3);
  });

  it("RG37_the_callers_team_scored_a_court_so_the_dealers_partner_deals", () => {
    expect(nextRungDealer(result(COURT_A))).toBe(1);
    expect(nextRungDealer(result({ team: 0, kind: "court52" }, SINGLE))).toBe(1);
    // Hokm: the caller keeps the call.
    expect(nextRungDealer(result(COURT_A, { courtPassesCall: false }))).toBe(3);
  });
});
