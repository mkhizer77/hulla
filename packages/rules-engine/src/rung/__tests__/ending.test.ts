import { describe, expect, it } from "vitest";

import { rungAutoPlayMove } from "../autoplay";
import { legalRungMoves } from "../engine";

import { SINGLE, apply, errorOf, playAll, stateFrom, types } from "./helpers";

import type { RungRules } from "../rules";
import type { RungSetup } from "../setup";

const HEARTS = "0:AH 1:2H 2:3H 3:4H";
const CLUBS_TO_1 = "0:2C 1:AC 2:3C 3:4C";

/** Seat 0 wins a heart trick, then seat 1 can win two club and diamond tricks. */
function position(setup: Partial<RungSetup> & { rules?: Partial<RungRules> }) {
  return stateFrom({
    hands: [
      ["AH", "2C", "2D"],
      ["2H", "AC", "AD"],
      ["3H", "3C", "3D"],
      ["4H", "4C", "4D"],
    ],
    trump: "S",
    ...setup,
  });
}

describe("how a deal ends", () => {
  it("RG24_RG25_single_sar_seven_tricks_against_at_least_one_win_the_deal_at_once", () => {
    const { state, events } = playAll(
      position({ rules: SINGLE, trickIndex: 9, counts: [6, 3] }),
      HEARTS,
    );
    expect(state.phase).toBe("ended");
    expect(state.turnSeat).toBeNull();
    expect(state.outcome).toEqual({ team: 0, kind: "win" });
    expect(events.at(-1)).toEqual({ type: "round_ended", team: 0, kind: "win", counts: [7, 3] });
    expect(legalRungMoves(state, 0)).toEqual([]);
    expect(errorOf(state, 0, { type: "play_card", card: "2C" })).toBe("rules.round_over");
  });

  it("RG24_six_tricks_do_not_end_the_deal", () => {
    const { state } = playAll(position({ rules: SINGLE, trickIndex: 7, counts: [5, 2] }), HEARTS);
    expect(state.phase).toBe("play");
    expect(state.counts).toEqual([6, 2]);
  });

  it("RG25_double_sar_seven_taken_against_at_least_one_ends_the_deal", () => {
    const start = position({ trickIndex: 7, counts: [5, 1], pile: 1, lastWinner: 0, run: 1 });
    const { state } = playAll(start, HEARTS);
    expect(state.counts).toEqual([7, 1]);
    expect(state.outcome).toEqual({ team: 0, kind: "win" });
  });

  it("RG31_single_sar_the_first_seven_tricks_are_a_court_when_nobody_may_chase", () => {
    const rules = { ...SINGLE, chase: false };
    const { state } = playAll(position({ rules, trickIndex: 6, counts: [6, 0] }), HEARTS);
    expect(state.outcome).toEqual({ team: 0, kind: "court" });
  });

  it("RG26_at_seven_against_zero_the_trick_winner_chooses_to_stop_or_play_on", () => {
    const { state, events } = playAll(
      position({ rules: SINGLE, trickIndex: 6, counts: [6, 0] }),
      HEARTS,
    );
    expect(state.phase).toBe("courtChoice");
    expect(state.turnSeat).toBe(0);
    expect(events.at(-1)).toEqual({ type: "court_choice_opened", seat: 0, team: 0 });
    expect(legalRungMoves(state, 0)).toEqual([
      { type: "court_choice", playOn: false },
      { type: "court_choice", playOn: true },
    ]);
    expect(legalRungMoves(state, 2)).toEqual([]);
    expect(errorOf(state, 2, { type: "court_choice", playOn: true })).toBe("rules.not_your_turn");
    expect(errorOf(state, 0, { type: "play_card", card: "2C" })).toBe("rules.wrong_phase");
  });

  it("RG26_stopping_scores_what_is_won_and_no_answer_means_stop", () => {
    const choice = playAll(
      position({ rules: SINGLE, trickIndex: 6, counts: [6, 0] }),
      HEARTS,
    ).state;
    expect(rungAutoPlayMove(choice, 0)).toEqual({ type: "court_choice", playOn: false });
    const { state, events } = apply(choice, 0, { type: "court_choice", playOn: false });
    expect(state.outcome).toEqual({ team: 0, kind: "court" });
    expect(types(events)).toEqual(["court_choice_made", "round_ended"]);
  });

  it("RGE8_a_court_choice_after_the_deal_ended_is_refused", () => {
    const choice = playAll(
      position({ rules: SINGLE, trickIndex: 6, counts: [6, 0] }),
      HEARTS,
    ).state;
    const ended = apply(choice, 0, { type: "court_choice", playOn: false }).state;
    expect(errorOf(ended, 0, { type: "court_choice", playOn: true })).toBe("rules.round_over");
  });

  it("RG27_single_sar_a_failed_chase_keeps_the_court", () => {
    const choice = playAll(
      position({ rules: SINGLE, trickIndex: 6, counts: [6, 0] }),
      HEARTS,
    ).state;
    const chase = apply(choice, 0, { type: "court_choice", playOn: true }).state;
    expect(chase.phase).toBe("play");
    expect(chase.chasing).toBe(0);
    expect(chase.turnSeat).toBe(0);
    const lost = playAll(chase, CLUBS_TO_1);
    expect(lost.state.counts).toEqual([7, 1]);
    expect(lost.state.outcome).toEqual({ team: 0, kind: "court" });
  });

  it("RG31_single_sar_all_thirteen_by_playing_on_are_a_52_court", () => {
    const start = stateFrom({
      rules: SINGLE,
      hands: [["AH"], ["2H"], ["3H"], ["4H"]],
      trump: "S",
      trickIndex: 12,
      counts: [12, 0],
      chasing: 0,
    });
    expect(playAll(start, HEARTS).state.outcome).toEqual({ team: 0, kind: "court52" });
  });

  it("RGE4_double_sar_reaching_seven_by_a_take_against_zero_opens_the_choice", () => {
    const start = position({ trickIndex: 6, counts: [5, 0], pile: 1, lastWinner: 0, run: 1 });
    const { state, events } = playAll(start, HEARTS);
    expect(state.counts).toEqual([7, 0]);
    expect(state.pile).toBe(0);
    expect(state.phase).toBe("courtChoice");
    expect(types(events).slice(-2)).toEqual(["pile_taken", "court_choice_opened"]);
    const stopped = apply(state, 0, { type: "court_choice", playOn: false }).state;
    expect(stopped.outcome).toEqual({ team: 0, kind: "win" });
  });

  it("RGE5_double_sar_without_the_chase_ends_at_seven_even_against_zero", () => {
    const start = position({
      rules: { chase: false },
      trickIndex: 6,
      counts: [5, 0],
      pile: 1,
      lastWinner: 0,
      run: 1,
    });
    expect(playAll(start, HEARTS).state.outcome).toEqual({ team: 0, kind: "win" });
  });

  it("RG27_double_sar_the_chase_ends_when_the_other_team_takes_a_pile", () => {
    const start = position({ trickIndex: 7, counts: [7, 0], chasing: 0, leader: 0 });
    const one = playAll(start, CLUBS_TO_1);
    expect(one.state.phase).toBe("play");
    expect(one.state.pile).toBe(1);
    const two = playAll(one.state, "1:AD 2:3D 3:4D 0:2D");
    expect(two.state.counts).toEqual([7, 2]);
    expect(two.state.outcome).toEqual({ team: 0, kind: "win" });
  });

  it("RG31_double_sar_all_thirteen_by_playing_on_are_a_court", () => {
    const start = stateFrom({
      hands: [["AH"], ["2H"], ["3H"], ["4H"]],
      trump: "S",
      trickIndex: 12,
      counts: [11, 0],
      pile: 1,
      lastWinner: 0,
      run: 1,
      chasing: 0,
    });
    expect(playAll(start, HEARTS).state.outcome).toEqual({ team: 0, kind: "court" });
    // Losing the last trick hands the pile over and the chase is only a win.
    const lost = playAll({ ...start, hands: [["2C"], ["AC"], ["3C"], ["4C"]] }, CLUBS_TO_1);
    expect(lost.state.counts).toEqual([11, 2]);
    expect(lost.state.outcome).toEqual({ team: 0, kind: "win" });
  });

  it("RG20_RG31_a_pile_of_all_thirteen_taken_on_the_last_trick_is_a_court_only_with_the_chase", () => {
    const setup = {
      hands: [["AH"], ["2H"], ["3H"], ["4H"]],
      trump: "S",
      trickIndex: 12,
      pile: 12,
      lastWinner: 1,
      run: 1,
    } as const;
    expect(playAll(stateFrom(setup), HEARTS).state.outcome).toEqual({ team: 0, kind: "court" });
    expect(playAll(stateFrom({ ...setup, rules: { chase: false } }), HEARTS).state.outcome).toEqual(
      { team: 0, kind: "win" },
    );
  });
});
