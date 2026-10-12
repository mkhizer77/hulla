import { Xoshiro128StarStar, type SeatIndex } from "@hulla/shared-ts";
import * as fc from "fast-check";
import { describe, expect, it } from "vitest";

import { createThullaRound } from "../deal";
import { applyMove, legalMoves } from "../engine";
import { THULLA_PHASES, onDeadline, thullaModule } from "../module";
import { createSeries, results, seriesUpdate } from "../results";
import { resolveRules } from "../rules";

import { stateFrom } from "./helpers";

import type { Card } from "../../card-kit";
import type { ThullaState } from "../state";

function ended(
  hands: readonly (readonly Card[])[],
  escapeOrder: SeatIndex[],
  bhabhi: SeatIndex,
  bigPenalty = false,
): ThullaState {
  const base = stateFrom({
    hands,
    leader: bhabhi,
    phase: "ended",
    rules: { bigBhabhiPenalty: bigPenalty },
  });
  return {
    ...base,
    escapeOrder,
    bhabhiSeat: bhabhi,
    seats: base.seats.map((seat, i) => ({
      ...seat,
      escaped: i !== bhabhi,
      escapeRank: i === bhabhi ? null : escapeOrder.indexOf(i) + 1,
    })),
  };
}

describe("results and series", () => {
  it("R49_R50_a_round_yields_the_escape_order_the_bhabhi_the_stats_and_baithak_points", () => {
    const state = ended([["2H", "3H"], ["9S"], ["TD"], ["JC"]], [3, 1, 2], 0);
    const result = results(state);
    expect(result).not.toBeNull();
    if (result === null) return;
    expect(result.finishOrder).toEqual([3, 1, 2, 0]);
    expect(result.loserSeat).toBe(0);
    expect(result.points).toEqual([-1, 2, 1, 3]);
    expect(result.bhabhiCards).toBe(2);
    expect(result.stats).toHaveLength(4);
    expect(results(stateFrom({ hands: [["2H"], ["3H"], ["4H"]], leader: 0 }))).toBeNull();
  });

  it("R50_with_five_or_six_seats_the_fourth_escape_scores_nothing_and_the_big_bhabhi_penalty_is_minus_two", () => {
    const five = ended(
      [["2H"], ["3H"], ["4H"], ["5H"], ["6H", "7H", "8H", "9H", "TH", "JH", "QH", "KH"]],
      [0, 1, 2, 3],
      4,
      true,
    );
    expect(results(five)?.points).toEqual([3, 2, 1, 0, -2]);
    const small = ended([["2H"], ["3H"], ["4H"], ["5H"], ["6H"]], [0, 1, 2, 3], 4, true);
    expect(results(small)?.points).toEqual([3, 2, 1, 0, -1]);
  });

  it("R51_R5_series_formats_accumulate_points_count_bhabhis_and_rotate_the_dealer", () => {
    const result = results(ended([["2H"], ["9S"], ["TD"]], [1, 2], 0));
    if (result === null) throw new Error("no result");
    let series = createSeries(3, 2);
    series = seriesUpdate(series, result, "session");
    expect(series).toEqual({
      roundsPlayed: 1,
      points: [-1, 3, 2],
      loserTally: [1, 0, 0],
      nextDealerSeat: 0,
      finished: false,
    });
    expect(seriesUpdate(createSeries(3, 0), result, "single").finished).toBe(true);
    let best = createSeries(3, 0);
    for (let i = 0; i < 3; i++) best = seriesUpdate(best, result, "bestOf3");
    expect(best.finished).toBe(true);
    expect(best.points).toEqual([-3, 9, 6]);
    let first = createSeries(3, 0);
    for (let i = 0; i < 2; i++) first = seriesUpdate(first, result, "firstTo3Bhabhis");
    expect(first.finished).toBe(false);
    first = seriesUpdate(first, result, "firstTo3Bhabhis");
    expect(first.finished).toBe(true);
    expect(first.loserTally).toEqual([3, 0, 0]);
  });

  it("R43_the_opening_is_a_simultaneous_window_and_play_is_a_per_seat_timer", () => {
    expect(THULLA_PHASES.map((phase) => [phase.id, phase.deadline.kind])).toEqual([
      ["opening", "simultaneousWindow"],
      ["play", "perSeatTimer"],
      ["ended", "none"],
    ]);
    expect(
      thullaModule.currentPhase(stateFrom({ hands: [["2H"], ["3H"], ["4H"]], leader: 0 })),
    ).toBe("play");
  });

  it("R44_onDeadline_always_returns_a_legal_move_for_a_seat_that_has_one_and_never_a_take", () => {
    const rules = resolveRules("private", "standard", { handTaking: "anyone" });
    if (!rules.ok) throw new Error(rules.error.code);
    fc.assert(
      fc.property(
        fc.integer({ min: 0, max: 255 }),
        fc.integer({ min: 3, max: 6 }),
        (seedByte, seatCount) => {
          const seed = new Uint8Array(32).fill(seedByte);
          const rng = Xoshiro128StarStar.fromSeed(seed);
          const round = createThullaRound(
            { rules: rules.value, seatCount, dealerSeat: 0, seed },
            rng,
          );
          if (!round.ok) throw new Error(round.error.code);
          let state = round.value;
          for (let step = 0; step < 300 && state.phase !== "ended"; step++) {
            let acted = false;
            for (let seat = 0; seat < seatCount && !acted; seat++) {
              const legal = legalMoves(state, seat);
              const move = onDeadline(state, seat);
              const playable = legal.filter((m) => m.type === "play_card");
              if (playable.length === 0) {
                expect(move).toBeNull();
                continue;
              }
              expect(move).not.toBeNull();
              expect(move?.type).toBe("play_card");
              expect(playable).toContainEqual(move);
              if (move === null) throw new Error("unreachable");
              const result = applyMove(state, seat, move, rng);
              expect(result.ok).toBe(true);
              if (result.ok) state = result.state;
              acted = true;
            }
            if (!acted) throw new Error("nobody could act");
          }
          expect(state.phase).toBe("ended");
        },
      ),
      { numRuns: 40 },
    );
  });

  it("the_module_object_implements_the_contract_end_to_end", () => {
    const seed = new Uint8Array(32).fill(21);
    const rng = Xoshiro128StarStar.fromSeed(seed);
    const rules = resolveRules("public");
    if (!rules.ok) throw new Error(rules.error.code);
    const round = thullaModule.createRound(
      { rules: rules.value, seatCount: 4, dealerSeat: 1, seed },
      rng,
    );
    expect(round.ok).toBe(true);
    if (!round.ok) return;
    expect(thullaModule.id).toBe("thulla");
    expect(thullaModule.seats).toEqual({ min: 3, max: 6 });
    expect(thullaModule.isRoundOver(round.value)).toBe(false);
    let state = round.value;
    for (let step = 0; step < 500 && !thullaModule.isRoundOver(state); step++) {
      const seat = state.turnSeat ?? state.openingPending[0] ?? state.leaderSeat;
      const move = thullaModule.onDeadline(state, seat);
      if (move === null) throw new Error("no move");
      const result = thullaModule.applyMove(state, seat, move, rng);
      if (!result.ok) throw new Error(result.error.code);
      state = result.state;
    }
    const result = thullaModule.results(state);
    expect(result.finishOrder).toHaveLength(4);
    const series = thullaModule.seriesUpdate(createSeries(4, 1), result);
    expect(series.roundsPlayed).toBe(1);
    expect(series.nextDealerSeat).toBe(2);
    expect(thullaModule.redact(state, { kind: "spectator" }).seedHex).not.toBeNull();
    expect(() => thullaModule.results(round.value)).toThrow();
  });
});
