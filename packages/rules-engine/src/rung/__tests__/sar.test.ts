import { describe, expect, it } from "vitest";

import { SINGLE, playAll, stateFrom, types } from "./helpers";

import type { RungRules } from "../rules";

/** Seat 0 holds the winners; everyone follows hearts, then clubs, then diamonds. */
function threeTricks(top: readonly [string, string, string], rules: Partial<RungRules> = {}) {
  return stateFrom({
    rules,
    hands: [
      [`${top[0]}H`, `${top[1]}C`, `${top[2]}D`],
      ["2H", "2C", "2D"],
      ["3H", "3C", "3D"],
      ["4H", "4C", "4D"],
    ] as never,
    trump: "S",
  });
}

const T1 = (top: string) => `0:${top}H 1:2H 2:3H 3:4H`;
const T2 = (top: string) => `0:${top}C 1:2C 2:3C 3:4C`;
const T3 = (top: string) => `0:${top}D 1:2D 2:3D 3:4D`;

describe("Single Sar", () => {
  it("RG15_a_trick_belongs_to_the_winners_team_at_once", () => {
    const { state, events } = playAll(threeTricks(["A", "K", "Q"], SINGLE), T1("A"));
    expect(state.counts).toEqual([1, 0]);
    expect(state.pile).toBe(0);
    expect(types(events)).not.toContain("pile_taken");
    expect(state.stats[0]?.tricksWon).toBe(1);
    expect(playAll(state, T2("K")).state.counts).toEqual([2, 0]);
  });
});

describe("Double Sar", () => {
  it("RG16_a_won_trick_goes_onto_the_pile_and_nobody_owns_it", () => {
    const { state, events } = playAll(threeTricks(["A", "K", "Q"]), T1("A"));
    expect(state.pile).toBe(1);
    expect(state.counts).toEqual([0, 0]);
    expect(state.lastWinner).toBe(0);
    expect(state.run).toBe(1);
    expect(events.at(-1)).toMatchObject({ type: "trick_won", winner: 0, pile: 1, counts: [0, 0] });
  });

  it("RG17_two_tricks_in_a_row_by_one_player_take_the_whole_pile", () => {
    const start = { ...threeTricks(["A", "K", "Q"]), pile: 3, counts: [1, 2] as const };
    const { state, events } = playAll(start, `${T1("A")} ${T2("K")}`);
    expect(state.counts).toEqual([6, 2]);
    expect(state.pile).toBe(0);
    expect(events.at(-1)).toEqual({
      type: "pile_taken",
      seat: 0,
      team: 0,
      tricks: 5,
      counts: [6, 2],
    });
    expect(state.stats[0]?.pilesTaken).toBe(1);
    expect(state.turnSeat).toBe(0);
  });

  it("RG18_two_tricks_in_a_row_by_the_two_partners_do_not_take_the_pile", () => {
    const start = stateFrom({
      hands: [
        ["AH", "2C", "6D"],
        ["2H", "3C", "5D"],
        ["3H", "AC", "KD"],
        ["4H", "4C", "7D"],
      ],
      trump: "S",
    });
    const two = playAll(start, "0:AH 1:2H 2:3H 3:4H 0:2C 1:3C 2:AC 3:4C");
    expect(two.state.counts).toEqual([0, 0]);
    expect(two.state.pile).toBe(2);
    expect(two.state.lastWinner).toBe(2);
    expect(two.state.run).toBe(1);
    // The second winner needs one more, and then takes all three.
    const three = playAll(two.state, "2:KD 3:7D 0:6D 1:5D");
    expect(three.state.counts).toEqual([3, 0]);
    expect(three.events.at(-1)).toMatchObject({ type: "pile_taken", seat: 2, tricks: 3 });
  });

  it("RG19_after_a_take_the_run_starts_again", () => {
    const taken = playAll(threeTricks(["A", "K", "Q"]), `${T1("A")} ${T2("K")}`).state;
    expect(taken.lastWinner).toBeNull();
    expect(taken.run).toBe(0);
    const next = playAll(taken, T3("Q"));
    expect(next.state.pile).toBe(1);
    expect(next.state.counts).toEqual([2, 0]);
    expect(types(next.events)).not.toContain("pile_taken");
  });

  it("RG20_the_winner_of_the_thirteenth_trick_takes_what_lies_in_the_pile", () => {
    const start = stateFrom({
      hands: [["AH"], ["2H"], ["3H"], ["4H"]],
      trump: "S",
      trickIndex: 12,
      counts: [5, 4],
      pile: 3,
      lastWinner: 1,
      run: 1,
    });
    const { state, events } = playAll(start, T1("A"));
    expect(state.counts).toEqual([9, 4]);
    expect(types(events).filter((type) => type === "pile_taken")).toHaveLength(1);
    expect(state.outcome).toEqual({ team: 0, kind: "win" });
  });

  it("RGE3_winning_tricks_twelve_and_thirteen_is_one_take", () => {
    const start = stateFrom({
      hands: [["AH"], ["2H"], ["3H"], ["4H"]],
      trump: "S",
      trickIndex: 12,
      counts: [5, 5],
      pile: 2,
      lastWinner: 0,
      run: 1,
    });
    const { state, events } = playAll(start, T1("A"));
    expect(events.filter((event) => event.type === "pile_taken")).toEqual([
      { type: "pile_taken", seat: 0, team: 0, tricks: 3, counts: [8, 5] },
    ]);
    expect(state.phase).toBe("ended");
  });

  it("RG21_the_winner_of_tricks_one_and_two_waits_for_trick_three", () => {
    const rules = { firstTwo: true };
    const two = playAll(threeTricks(["A", "K", "Q"], rules), `${T1("A")} ${T2("K")}`);
    expect(two.state.counts).toEqual([0, 0]);
    expect(two.state.pile).toBe(2);
    expect(two.state.run).toBe(2);
    const three = playAll(two.state, T3("Q"));
    expect(three.state.counts).toEqual([3, 0]);
    expect(three.events.at(-1)).toMatchObject({ type: "pile_taken", tricks: 3 });
  });

  it("RG21_tricks_two_and_three_take_all_three_and_the_rule_is_off_by_default", () => {
    const start = stateFrom({
      rules: { firstTwo: true },
      hands: [
        ["2H", "AC", "AD"],
        ["AH", "2C", "2D"],
        ["3H", "3C", "3D"],
        ["4H", "4C", "4D"],
      ],
      trump: "S",
    });
    const { state } = playAll(start, "0:2H 1:AH 2:3H 3:4H 1:2C 2:3C 3:4C 0:AC 0:AD 1:2D 2:3D 3:4D");
    expect(state.counts).toEqual([3, 0]);
    const plain = playAll(threeTricks(["A", "K", "Q"]), `${T1("A")} ${T2("K")}`);
    expect(plain.state.counts).toEqual([2, 0]);
  });

  it("RG22_two_aces_in_a_row_do_not_take_the_pile_and_the_next_win_does", () => {
    const rules = { aceRule: true };
    const two = playAll(threeTricks(["A", "A", "K"], rules), `${T1("A")} ${T2("A")}`);
    expect(two.state.counts).toEqual([0, 0]);
    expect(two.state.pile).toBe(2);
    expect(two.state.runAceBlocked).toBe(true);
    const three = playAll(two.state, T3("K"));
    expect(three.state.counts).toEqual([3, 0]);
    // A third ace takes it too: the rule refuses a run once.
    const aces = playAll(threeTricks(["A", "A", "A"], rules), `${T1("A")} ${T2("A")} ${T3("A")}`);
    expect(aces.state.counts).toEqual([3, 0]);
  });

  it("RG22_an_ace_and_another_card_take_the_pile_as_usual", () => {
    const rules = { aceRule: true };
    expect(
      playAll(threeTricks(["A", "K", "Q"], rules), `${T1("A")} ${T2("K")}`).state.counts,
    ).toEqual([2, 0]);
    expect(
      playAll(threeTricks(["K", "A", "Q"], rules), `${T1("K")} ${T2("A")}`).state.counts,
    ).toEqual([2, 0]);
    // Without the rule two aces take it.
    expect(playAll(threeTricks(["A", "A", "K"]), `${T1("A")} ${T2("A")}`).state.counts).toEqual([
      2, 0,
    ]);
  });

  it("RGE6_aces_on_tricks_four_and_five_then_a_king_takes_the_pile_on_six", () => {
    const start = {
      ...threeTricks(["A", "A", "K"], { aceRule: true }),
      trickIndex: 3,
      counts: [2, 1] as const,
    };
    const five = playAll(start, `${T1("A")} ${T2("A")}`);
    expect(five.state.counts).toEqual([2, 1]);
    const six = playAll(five.state, T3("K"));
    expect(six.state.counts).toEqual([5, 1]);
    expect(six.state.trickIndex).toBe(6);
  });

  it("RGE7_first_two_and_the_ace_rule_together_still_take_on_trick_three", () => {
    const rules = { aceRule: true, firstTwo: true };
    for (const third of ["A", "K"]) {
      const { state } = playAll(
        threeTricks(["A", "A", third], rules),
        `${T1("A")} ${T2("A")} ${T3(third)}`,
      );
      expect(state.counts).toEqual([3, 0]);
    }
  });

  it("RG23_tricks_eleven_and_twelve_do_not_take_the_pile_and_thirteen_decides_it", () => {
    const start = stateFrom({
      rules: { lastTwo: true },
      hands: [
        ["AH", "2C"],
        ["2H", "AC"],
        ["3H", "3C"],
        ["4H", "4C"],
      ],
      trump: "S",
      trickIndex: 11,
      counts: [5, 4],
      pile: 2,
      lastWinner: 0,
      run: 1,
    });
    const twelve = playAll(start, T1("A"));
    expect(twelve.state.counts).toEqual([5, 4]);
    expect(twelve.state.pile).toBe(3);
    const thirteen = playAll(twelve.state, "0:2C 1:AC 2:3C 3:4C");
    expect(thirteen.state.counts).toEqual([5, 8]);
    expect(thirteen.state.outcome).toEqual({ team: 1, kind: "win" });
    // Without the rule trick twelve takes the pile and the deal ends there.
    const plain = playAll({ ...start, rules: { ...start.rules, lastTwo: false } }, T1("A"));
    expect(plain.state.counts).toEqual([8, 4]);
    expect(plain.state.phase).toBe("ended");
  });
});
