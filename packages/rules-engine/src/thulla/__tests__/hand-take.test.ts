import { describe, expect, it } from "vitest";

import { applyMove, canTakeHand, legalMoves } from "../engine";

import { fixedRng, handOf, stateFrom, types } from "./helpers";

const take = { type: "take_hand" } as const;

describe("taking a hand", () => {
  it("R31_hand_taking_is_off_in_public_and_ranked_and_leader_only_in_private_by_default", () => {
    const pub = stateFrom({ hands: [["2H"], ["3H"], ["4H"]], leader: 0, mode: "public" });
    const result = applyMove(pub, 0, take, fixedRng());
    expect(!result.ok && result.error.code).toBe("rules.hand_take_disabled");
    const priv = stateFrom({ hands: [["2H"], ["3H"], ["4H"]], leader: 0 });
    expect(canTakeHand(priv, 0)).toBe(true);
    expect(canTakeHand(priv, 1)).toBe(false);
  });

  it("R32_the_leader_may_take_before_leading_from_trick_two_onward", () => {
    const trickOne = stateFrom({ hands: [["2H"], ["3H"], ["4H"]], leader: 0, trickIndex: 0 });
    const early = applyMove(trickOne, 0, take, fixedRng());
    expect(!early.ok && early.error.code).toBe("rules.hand_take_window_closed");
    const state = stateFrom({ hands: [["2H", "9S"], ["3H"], ["4H"]], leader: 0 });
    expect(legalMoves(state, 0)).toContainEqual(take);
    expect(legalMoves(state, 1)).toEqual([]);
    const notLeader = applyMove(state, 1, take, fixedRng());
    expect(!notLeader.ok && notLeader.error.code).toBe("rules.hand_take_not_leader");
  });

  it("R33_every_card_moves_to_the_taker_the_target_escapes_and_the_taker_leads", () => {
    // Appendix C, V-070.
    const state = stateFrom({
      hands: [
        ["5H", "6H"],
        ["7H", "8H"],
        ["2C", "4D", "9S"],
        ["TH", "JH"],
      ],
      leader: 1,
      trickIndex: 3,
    });
    const result = applyMove(state, 1, take, fixedRng());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(types(result.events)).toEqual(["hand_taken", "escape"]);
    expect(result.events[0]).toEqual({ type: "hand_taken", taker: 1, target: 2, cardCount: 3 });
    expect(handOf(result.state, 1)).toEqual(["9S", "7H", "8H", "2C", "4D"]);
    expect(result.state.seats[2]).toMatchObject({ escaped: true, escapeRank: 1, hand: [] });
    expect(result.state.leaderSeat).toBe(1);
    expect(result.state.turnSeat).toBe(1);
    expect(result.state.handTakeUsed).toBe(true);
    expect(result.state.stats[1]?.handsTaken).toBe(1);
  });

  it("E11_the_lead_closes_the_window", () => {
    const state = stateFrom({ hands: [["2H", "9S"], ["3H"], ["4H"]], leader: 0 });
    const led = applyMove(state, 0, { type: "play_card", card: "2H" }, fixedRng());
    const late = led.ok && applyMove(led.state, 0, take, fixedRng());
    expect(late && !late.ok && late.error.code).toBe("rules.hand_take_window_closed");
  });

  it("R35_E10_in_anyone_mode_any_active_seat_may_take_and_the_first_accepted_wins", () => {
    const state = stateFrom({
      hands: [["2H", "9S"], ["3H"], ["4H", "5H"], ["6H"]],
      leader: 0,
      rules: { handTaking: "anyone" },
    });
    expect(legalMoves(state, 2)).toEqual([take]);
    const result = applyMove(state, 2, take, fixedRng());
    expect(result.ok).toBe(true);
    if (!result.ok) return;
    expect(result.events[0]).toMatchObject({ taker: 2, target: 3, cardCount: 1 });
    expect(result.state.leaderSeat).toBe(2);
    const second = applyMove(result.state, 0, take, fixedRng());
    expect(!second.ok && second.error.code).toBe("rules.hand_take_window_closed");
    expect(legalMoves(result.state, 2)).not.toContainEqual(take);
  });

  it("R36_hand_taking_is_disabled_with_exactly_two_active_players", () => {
    // Appendix C, V-071.
    const state = stateFrom({ hands: [["2H", "9S"], ["3H"]], leader: 0 });
    const result = applyMove(state, 0, take, fixedRng());
    expect(!result.ok && result.error.code).toBe("rules.hand_take_disabled_endgame");
    expect(legalMoves(state, 0)).not.toContainEqual(take);
  });

  it("E8_a_target_with_one_card_can_be_taken_and_escapes_with_that_rank", () => {
    const state = stateFrom({ hands: [["2H"], ["3H"], ["4H", "5H"]], leader: 0 });
    const result = applyMove(state, 0, take, fixedRng());
    expect(result.ok && result.events[1]).toEqual({ type: "escape", seat: 1, rank: 1 });
    expect(result.ok && handOf(result.state, 0)).toEqual(["2H", "3H"]);
  });

  it("E9_a_take_that_leaves_two_active_starts_the_endgame_and_disables_taking", () => {
    const state = stateFrom({ hands: [["2H"], ["3H"], ["4H", "5H"]], leader: 0 });
    const result = applyMove(state, 0, take, fixedRng());
    expect(result.ok && types(result.events)).toEqual(["hand_taken", "escape", "endgame_started"]);
    const again = result.ok && applyMove(result.state, 0, take, fixedRng());
    expect(again && !again.ok && again.error.code).toBe("rules.hand_take_disabled_endgame");
  });

  it("R26_a_leader_who_just_drew_must_lead_the_drawn_card_and_cannot_take", () => {
    const state = stateFrom({ hands: [["8D"], ["3H"], ["4H"]], leader: 0, pendingAutoLead: true });
    expect(legalMoves(state, 0)).toEqual([{ type: "play_card", card: "8D" }]);
    const result = applyMove(state, 0, take, fixedRng());
    expect(!result.ok && result.error.code).toBe("rules.hand_take_window_closed");
  });

  it("R34_a_take_is_never_part_of_the_moves_for_an_escaped_seat", () => {
    const state = stateFrom({
      hands: [["2H"], [], ["4H", "5H"], ["6H"]],
      leader: 0,
      rules: { handTaking: "anyone" },
    });
    expect(legalMoves(state, 1)).toEqual([]);
    const result = applyMove(state, 1, take, fixedRng());
    expect(!result.ok && result.error.code).toBe("rules.seat_not_active");
  });
});
