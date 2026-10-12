import { Xoshiro128StarStar } from "@hulla/shared-ts";
import { describe, expect, it } from "vitest";

import { createThullaRound } from "../deal";
import { applyMove, legalMoves } from "../engine";
import { defaultRules } from "../rules";

import { fixedRng, handOf, stateFrom, types } from "./helpers";

import type { Card } from "../../card-kit";
import type { ThullaState } from "../state";

function freshRound(seatCount = 4): ThullaState {
  const seed = new Uint8Array(32).fill(5);
  const result = createThullaRound(
    { rules: defaultRules("private"), seatCount, dealerSeat: 0, seed },
    Xoshiro128StarStar.fromSeed(seed),
  );
  if (!result.ok) {
    throw new Error(result.error.code);
  }
  return result.value;
}

function play(state: ThullaState, seat: number, card: Card): ThullaState {
  const result = applyMove(state, seat, { type: "play_card", card }, fixedRng());
  if (!result.ok) {
    throw new Error(`${result.error.code} for seat ${seat} card ${card}`);
  }
  return result.state;
}

describe("the opening trick", () => {
  it("R12_only_the_ace_of_spades_is_legal_for_the_opening_leader", () => {
    const state = freshRound();
    const leader = state.leaderSeat;
    expect(legalMoves(state, leader)).toEqual([{ type: "play_card", card: "AS" }]);
    const other = handOf(state, leader).find((card) => card !== "AS");
    const wrong = applyMove(state, leader, { type: "play_card", card: other ?? "2H" }, fixedRng());
    expect(!wrong.ok && wrong.error.code).toBe("rules.must_lead_ace_of_spades");
    const someoneElse = (leader + 1) % 4;
    expect(legalMoves(state, someoneElse)).toEqual([]);
    const early = applyMove(
      state,
      someoneElse,
      { type: "play_card", card: handOf(state, someoneElse)[0] ?? "2H" },
      fixedRng(),
    );
    expect(!early.ok && early.error.code).toBe("rules.not_your_turn");
  });

  it("R13_the_others_respond_in_any_order_and_must_follow_spades", () => {
    const state = stateFrom({
      hands: [
        ["AS", "2H"],
        ["3S", "4H"],
        ["5H", "6D"],
        ["7S", "8S"],
      ],
      leader: 0,
      trickIndex: 0,
      phase: "opening",
      openingPending: [1, 2, 3],
    });
    const led = play(state, 0, "AS");
    expect(led.turnSeat).toBeNull();
    expect(led.openingPending).toEqual([1, 2, 3]);
    expect(legalMoves(led, 1)).toEqual([{ type: "play_card", card: "3S" }]);
    expect(legalMoves(led, 2).map((m) => (m.type === "play_card" ? m.card : ""))).toEqual([
      "5H",
      "6D",
    ]);
    expect(legalMoves(led, 3)).toHaveLength(2);
    const offSuit = applyMove(led, 1, { type: "play_card", card: "4H" }, fixedRng());
    expect(!offSuit.ok && offSuit.error.code).toBe("rules.must_follow_suit");
    const lateFirst = play(led, 3, "8S");
    expect(lateFirst.openingPending).toEqual([1, 2]);
    const again = applyMove(lateFirst, 3, { type: "play_card", card: "7S" }, fixedRng());
    expect(!again.ok && again.error.code).toBe("rules.not_your_turn");
  });

  it("R14_the_opening_trick_never_causes_a_pickup_and_the_ace_holder_leads_trick_two", () => {
    const state = stateFrom({
      hands: [
        ["AS", "2H"],
        ["3S", "4H"],
        ["5H", "6D"],
        ["7S", "8S"],
      ],
      leader: 0,
      trickIndex: 0,
      phase: "opening",
      openingPending: [1, 2, 3],
    });
    let next = play(state, 0, "AS");
    next = play(next, 2, "6D");
    next = play(next, 1, "3S");
    const result = applyMove(next, 3, { type: "play_card", card: "7S" }, fixedRng());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(types(result.events)).toEqual(["card_played", "trick_resolved"]);
    expect(result.events[1]).toMatchObject({ picker: null, thullaBy: null, nextLeader: 0 });
    expect(result.state.discard).toEqual(["AS", "6D", "3S", "7S"]);
    expect(result.state.leaderSeat).toBe(0);
    expect(result.state.turnSeat).toBe(0);
    expect(result.state.trickIndex).toBe(1);
    expect(result.state.phase).toBe("play");
    expect(handOf(result.state, 2)).toEqual(["5H"]);
  });

  it("E2_two_off_suit_plays_in_the_opening_still_cause_no_pickup", () => {
    const state = stateFrom({
      hands: [
        ["AS", "2H"],
        ["4H", "5H"],
        ["6D", "7D"],
        ["7S", "8S"],
      ],
      leader: 0,
      trickIndex: 0,
      phase: "opening",
      openingPending: [1, 2, 3],
    });
    let next = play(state, 0, "AS");
    next = play(next, 1, "4H");
    next = play(next, 2, "6D");
    const result = applyMove(next, 3, { type: "play_card", card: "7S" }, fixedRng());
    expect(result.ok && result.events[1]).toMatchObject({ type: "trick_resolved", picker: null });
    expect(result.ok && result.state.discard).toHaveLength(4);
    expect(result.ok && result.events[1]).not.toMatchObject({ thullaBy: 1 });
  });

  it("R15_opening_thulla_applies_turn_order_and_the_opener_picks_up", () => {
    const state = stateFrom({
      hands: [
        ["AS", "2H"],
        ["4H", "5H"],
        ["6S", "7D"],
        ["7S", "8S"],
      ],
      leader: 0,
      trickIndex: 0,
      phase: "opening",
      preset: "hardcore",
      rules: { thullaEndsTrick: true },
    });
    const led = play(state, 0, "AS");
    expect(led.turnSeat).toBe(1);
    expect(legalMoves(led, 2)).toEqual([]);
    const result = applyMove(led, 1, { type: "play_card", card: "4H" }, fixedRng());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.events[0]).toMatchObject({ type: "card_played", thulla: true });
    expect(result.events[1]).toMatchObject({ type: "trick_resolved", picker: 0, thullaBy: 1 });
    expect(handOf(result.state, 0)).toEqual(["AS", "2H", "4H"]);
    expect(result.state.leaderSeat).toBe(0);
    expect(result.state.phase).toBe("play");
  });

  it("opening in turn order without the thulla toggle treats an off-suit play as a plain play", () => {
    const state = stateFrom({
      hands: [
        ["AS", "2H"],
        ["4H", "5H"],
        ["6S", "7D"],
      ],
      leader: 0,
      trickIndex: 0,
      phase: "opening",
      rules: { openingSimultaneous: false },
    });
    const led = play(state, 0, "AS");
    expect(led.turnSeat).toBe(1);
    const next = play(led, 1, "4H");
    expect(next.trick.thullaBy).toBeNull();
    expect(next.turnSeat).toBe(2);
    const result = applyMove(next, 2, { type: "play_card", card: "6S" }, fixedRng());
    expect(result.ok && result.state.discard).toEqual(["AS", "4H", "6S"]);
  });
});
